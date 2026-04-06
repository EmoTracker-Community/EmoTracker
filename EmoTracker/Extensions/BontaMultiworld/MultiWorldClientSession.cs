using EmoTracker.Data.AutoTracking;
using EmoTracker.Core;
using EmoTracker.Core.Services;
using EmoTracker.Data;
using EmoTracker.Data.JSON;
using EmoTracker.Data.Packages;
using EmoTracker.Extensions.AutoTracker;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using WebSocketSharp;

namespace EmoTracker.Extensions.BontaMultiworld
{
    #region -- Command Attribute --

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class CommandAttribute : Attribute
    {
        public string Command
        {
            get; set;
        }

        public CommandAttribute(string command)
        {
            Command = command;
        }
    }

    #endregion

    public enum SessionLoginType
    {
        Unknown,
        Legacy,
        RomBased
    }

    public enum SessionStatus
    {
        Disconnected,
        Connecting,
        Connected,
        Authenticated,
        Ready
    }

    [Flags]
    public enum AuthenticationFailure
    {
        None = 0x00,
        InvalidName = 0x01,
        InvalidPassword = 0x02,
        InvalidTeam = 0x04,
        InvalidSlot = 0x08,
        SlotAlreadyTaken = 0x10,
        NameAlreadyTaken = 0x20,
        InvalidRom = 0x40
    }

    public enum SessionError
    {
        None,
        ConnectionError,
        ProtocolError,
        RomValidationError
    }

    public class MultiWorldClientSession : ObservableObject
    {
        #region -- Message Log --

        protected ObservableCollection<string> mMessageLog = new ObservableCollection<string>();
        public IReadOnlyList<string> MessageLog
        {
            get { return mMessageLog; }
        }

        public void ClearMessageLog(object arg = null)
        {
            Dispatch.BeginInvoke(() =>
            {
                mMessageLog.Clear();
            });
        }

        public void Log(string format, params object[] tokens)
        {
            string formattedMsg = string.Format(format, tokens);
            Dispatch.BeginInvoke(() =>
            {
                mMessageLog.Add(formattedMsg);
            });
        }

        #endregion

        #region -- Socket --

        WebSocket mSocket;
        protected WebSocket Socket
        {
            get { return mSocket; }
            set
            {
                WebSocket existingSocket = mSocket;
                if (SetProperty(ref mSocket, value))
                {
                    if (existingSocket != null)
                    {
                        try
                        {
                            existingSocket.OnOpen -= Socket_OnOpen;
                            existingSocket.OnClose -= Socket_OnClose;
                            existingSocket.OnError -= Socket_OnError;
                            existingSocket.OnMessage -= Socket_OnMessage;
                            existingSocket.Close();
                        }
                        catch { }
                    }

                    if (mSocket != null)
                    {
                        mSocket.OnOpen += Socket_OnOpen;
                        mSocket.OnClose += Socket_OnClose;
                        mSocket.OnError += Socket_OnError;
                        mSocket.OnMessage += Socket_OnMessage;
                    }
                }
            }
        }

        #endregion

        #region -- Status Info --

        SessionLoginType mSessionLoginType = SessionLoginType.Unknown;
        public SessionLoginType SessionLoginType
        {
            get { return mSessionLoginType; }
            set { SetProperty(ref mSessionLoginType, value); }
        }

        SessionStatus mSessionStatus = SessionStatus.Disconnected;
        public SessionStatus SessionStatus
        {
            get { return mSessionStatus; }
            set { SetProperty(ref mSessionStatus, value); }
        }

        SessionError mSessionError = SessionError.None;
        public SessionError Error
        {
            get { return mSessionError; }
            set { SetProperty(ref mSessionError, value); }
        }

        AuthenticationFailure mAuthFailure = AuthenticationFailure.None;
        public AuthenticationFailure AuthenticationFailure
        {
            get { return mAuthFailure; }
            set { SetProperty(ref mAuthFailure, value); }
        }

        #endregion

        #region -- Session Description --

        public class SessionDescription : ObservableObject
        {
            bool mbPasswordRequired = false;
            public bool PasswordRequired
            {
                get { return mbPasswordRequired; }
                private set { SetProperty(ref mbPasswordRequired, value); }
            }

            public class Slot : ObservableObject
            {
                string mPlayerName;
                string mTeamName;
                int mIndex = -1;
                bool mbAvailable = true;

                public string PlayerName
                {
                    get { return mPlayerName; }
                    set { SetProperty(ref mPlayerName, value); }
                }

                public string TeamName
                {
                    get { return mTeamName; }
                    set { SetProperty(ref mTeamName, value); }
                }

                [DependentProperty("DisplayIndex")]
                public int Index
                {
                    get { return mIndex; }
                    set { SetProperty(ref mIndex, value); }
                }

                public int ServerIndex
                {
                    get { return Index; }
                }

                public int DisplayIndex
                {
                    get { return Index; }
                }

                public bool Available
                {
                    get { return mbAvailable; }
                    set { SetProperty(ref mbAvailable, value); }
                }
            }

            ObservableCollection<Slot> mSlots = new ObservableCollection<Slot>();
            public IReadOnlyList<Slot> Slots
            {
                get { return mSlots; }
            }

            public int PlayerCount
            {
                get { return mSlots.Count; }
                private set
                {
                    mSlots.Clear();
                    for (int i = 0; i < value; ++i)
                    {
                        mSlots.Add(new Slot()
                        {
                            Index = i + 1
                        });
                    }
                }
            }

            public SessionDescription(JToken data, MultiWorldClientSession session)
            {
                JObject dataObject = data as JObject;

                if (dataObject == null)
                    throw new InvalidDataException("Unsupported data format for session description");

                PlayerCount = dataObject.GetValue<int>("slots");
                PasswordRequired = dataObject.GetValue<bool>("password", false);

                if (PlayerCount > 0 || PasswordRequired)
                {
                    session.Log("--------------------------------");
                    session.Log("Room Information:");
                    session.Log("--------------------------------");
                }

                if (PlayerCount > 0)
                    session.Log("{0} player seed", PlayerCount);

                if (PasswordRequired)
                    session.Log("Password Required");
            }
        }

        SessionDescription mDescription;
        public SessionDescription Description
        {
            get { return mDescription; }
            protected set { SetProperty(ref mDescription, value); }
        }

        #endregion

        #region -- Authentication --

        byte[] mExpectedRom;

        string mHostURI = "127.0.0.1:38281";
        public string HostUri
        {
            get { return mHostURI; }
            set { SetProperty(ref mHostURI, value); }
        }

        string mUserName;
        public string UserName
        {
            get { return mUserName; }
            set { SetProperty(ref mUserName, value); }
        }

        string mPassword;
        public string Password
        {
            get { return mPassword; }
            set { SetProperty(ref mPassword, value); }
        }

        string mUserTeam;
        public string UserTeam
        {
            get { return mUserTeam; }
            set { SetProperty(ref mUserTeam, value); }
        }

        SessionDescription.Slot mUserSlot;
        public SessionDescription.Slot UserSlot
        {
            get { return mUserSlot; }
            set { SetProperty(ref mUserSlot, value); }
        }

        protected bool CanAuthenticate(object arg = null)
        {
            if (SessionStatus != SessionStatus.Connected)
                return false;

            if (Description == null)
                return false;

            if (Description.PasswordRequired && string.IsNullOrWhiteSpace(Password))
                return false;

            if (SessionLoginType == SessionLoginType.Legacy)
            {
                if (string.IsNullOrWhiteSpace(UserName))
                    return false;

                if (UserSlot == null || !UserSlot.Available)
                    return false;
            }

            return true;
        }

        protected void Authenticate(object arg = null)
        {
            if (CanAuthenticate())
            {
                object abstractMessage;

                if (SessionLoginType == SessionLoginType.Legacy)
                {
                    LegacyConnectMsg msg = new LegacyConnectMsg();
                    msg.name = UserName;
                    msg.password = !string.IsNullOrWhiteSpace(Password) ? Password : null;
                    msg.team = !string.IsNullOrWhiteSpace(UserTeam) ? UserTeam : null;
                    msg.slot = UserSlot.Index;

                    abstractMessage = msg;
                }
                else
                {
                    ConnectMsg msg = new ConnectMsg();

                    mExpectedRom = GetRomHash();
                    msg.rom = new List<byte>(mExpectedRom);
                    msg.password = !string.IsNullOrWhiteSpace(Password) ? Password : null;

                    abstractMessage = msg;
                }

                Message message = new Message()
                {
                    Command = "Connect",
                    Data = JObject.FromObject(abstractMessage)
                };

                SendMessages(JoinSessionCompletionHandler, message);
            }
        }

        Dictionary<int, string> mTeamPlayerNameMap = new Dictionary<int, string>();

        [Command("Connected")]
        private void OnAuthenticatedCmd(JToken data)
        {
            AutoTrackerExtension extension = ExtensionManager.Instance.FindExtension<AutoTrackerExtension>();
            if (extension != null)
            {
                if (SessionLoginType == SessionLoginType.Legacy)
                {
                    JArray romContainer = data as JArray;
                    if (romContainer != null && romContainer.Count <= 0x15)
                    {
                        mExpectedRom = new byte[romContainer.Count];
                        for (int i = 0; i < romContainer.Count; ++i)
                        {
                            mExpectedRom[i] = romContainer[i].GetValue<byte>();
                        }
                    }
                    else
                    {
                        mExpectedRom = null;
                    }

                    if (!IsRomValid())
                    {
                        Disconnect(SessionError.RomValidationError);
                        return;
                    }
                }
                else
                {
                    try
                    {
                        JArray root = data as JArray;
                        if (root != null && root.Count >= 2)
                        {
                            JArray playerMap = root[1] as JArray;
                            if (playerMap != null)
                            {
                                foreach (JArray entry in playerMap)
                                {
                                    try
                                    {
                                        int playerIdx = entry[0].GetValue<int>();
                                        string playerName = entry[1].GetValue<string>();

                                        mTeamPlayerNameMap[playerIdx] = playerName;
                                    }
                                    catch
                                    {
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                mMemoryTimer = extension.AddMemoryTimer("Received Items Hook", WriteReceivedItems, 1000);
                MemorySegment.OnMemorySegmentUpdated += MemorySegment_OnMemorySegmentUpdated;

                SessionStatus = SessionStatus.Authenticated;

                SendLocationChecks(mCheckedLocations);
            }
        }

        [Command("ConnectionRefused")]
        private void OnAuthenticationFailedCmd(JToken data)
        {
            JArray serverErrors = data as JArray;
            if (serverErrors != null && serverErrors.Count > 0)
            {
                AuthenticationFailure failureState = AuthenticationFailure.None;

                foreach (JToken serverError in serverErrors)
                {
                    string errorCode = serverError.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(errorCode))
                    {
                        switch (errorCode)
                        {
                            case "InvalidRom":
                                failureState = failureState | AuthenticationFailure.InvalidRom;
                                break;

                            case "InvalidPassword":
                                failureState = failureState | AuthenticationFailure.InvalidPassword;
                                break;

                            case "InvalidName":
                                failureState = failureState | AuthenticationFailure.InvalidName;
                                break;

                            case "NameAlreadyTaken":
                                failureState = failureState | AuthenticationFailure.NameAlreadyTaken;
                                break;

                            case "InvalidTeam":
                                failureState = failureState | AuthenticationFailure.InvalidTeam;
                                break;

                            case "InvalidSlot":
                                failureState = failureState | AuthenticationFailure.InvalidSlot;
                                break;

                            case "SlotAlreadyTaken":
                                failureState = failureState | AuthenticationFailure.SlotAlreadyTaken;
                                break;
                        }
                    }
                }

                AuthenticationFailure = failureState;

                if (failureState != AuthenticationFailure.None)
                    Password = null;

                if (failureState.HasFlag(AuthenticationFailure.InvalidRom))
                    Disconnect(SessionError.RomValidationError);
            }
            else
            {
                Disconnect(SessionError.ProtocolError);
            }
        }

        #endregion

        #region -- Core Socket Handlers --

        Dictionary<string, MethodInfo> mMethodCache = new Dictionary<string, MethodInfo>();
               
        private void InvokeCommand(string command, JToken data)
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                MethodInfo method = null;
                if (!mMethodCache.TryGetValue(command, out method))
                {
                    bool bFound = false;

                    Type currentType = this.GetType();
                    while (currentType != null && !bFound)
                    {
                        var methods = currentType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        foreach (MethodInfo candidateMethod in methods)
                        {
                            CommandAttribute cmdAttr = candidateMethod.GetCustomAttribute<CommandAttribute>();
                            if (cmdAttr != null && string.Equals(command, cmdAttr.Command, StringComparison.OrdinalIgnoreCase))
                            {
                                bFound = true;
                                method = candidateMethod;
                                mMethodCache[command] = method;
                                break;
                            }
                        }

                        currentType = currentType.BaseType;
                    }

                    //  Cache null to avoid searching again
                    if (!bFound)
                        mMethodCache[command] = null;
                }

                if (method != null)
                    method.Invoke(this, new object[] { data });
                else
                    Log("Unknown Command: {0} :: {1}", command, data != null ? data.ToString() : "<no data>");
            }
        }

        private void Socket_OnMessage(object sender, MessageEventArgs e)
        {
            try
            {

                using (StringReader reader = new StringReader(e.Data))
                {
                    JArray commandList = JToken.ReadFrom(new JsonTextReader(reader)) as JArray;
                    if (commandList != null)
                    {
                        foreach (JArray command in commandList)
                        {
                            string cmd = null;
                            JToken data = null;

                            try
                            {
                                if (command.Count > 0)
                                    cmd = command[0].Value<string>();

                                if (command.Count > 1)
                                    data = command[1];

                                InvokeCommand(cmd, data);
                            }
                            catch (Exception ex)
                            {
                                Log("Exception occured while processing command: {0}", e.Data);
                                Log(ex.ToString());
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void Socket_OnClose(object sender, CloseEventArgs e)
        {
            System.Diagnostics.Debug.Print("Session Closed: {0}", e.Reason);
            Disconnect();
        }

        private void Socket_OnError(object sender, WebSocketSharp.ErrorEventArgs e)
        {
            System.Diagnostics.Debug.Print("Session Error: {0}", e.Message);
            Disconnect(SessionError.ConnectionError);
        }

        private void Socket_OnOpen(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.Print("Session Opened");
        }

#endregion

        #region -- Message Send --

        class Message
        {
            public string Command;
            public JToken Data;
        }

        private void SendMessages(Action<bool> completionHandler, params Message[] msgs)
        {
            SendMessages(msgs.AsEnumerable(), completionHandler);
        }

        private void SendMessages(IEnumerable<Message> msgs, Action<bool> completionHandler)
        {
            JArray batch = new JArray();
            foreach (Message msg in msgs)
            {
                if (msg != null)
                {
                    JArray msgArray = new JArray();
                    msgArray.Add(JToken.FromObject(msg.Command));
                    if (msg.Data != null)
                        msgArray.Add(msg.Data);

                    batch.Add(msgArray);
                }
            }

            if (batch.Count > 0)
            {
                using (StringWriter writer = new StringWriter())
                {
                    using (JsonTextWriter jsonWriter = new JsonTextWriter(writer))
                    {
                        jsonWriter.AutoCompleteOnClose = true;
                        jsonWriter.Formatting = Formatting.None;

                        jsonWriter.WriteToken(batch.CreateReader());
                        jsonWriter.Close();
                    }

                    string messageString = writer.ToString();

                    if (Socket != null)
                    {
                        lock (Socket)
                        {
                            Socket.SendAsync(messageString, completionHandler);
                        }
                    }
                }
            }
        }

#endregion

        public bool CanConnect(object arg = null)
        {
            if (Socket != null)
                return false;

            if (string.IsNullOrWhiteSpace(HostUri))
                return false;

            AutoTrackerExtension autotracker = ExtensionManager.Instance.FindExtension<AutoTrackerExtension>();
            if (autotracker == null || autotracker.ActiveProvider == null || !autotracker.ActiveProvider.IsConnected)
                return false;

            return true;
        }

        public void Connect()
        {
            try
            {
                if (!CanConnect())
                    return;

                string uri = HostUri;
                if (!uri.StartsWith("ws://") && !uri.StartsWith("wss://"))
                    uri = "ws://" + uri;

                Socket = new WebSocket(uri);
                SessionStatus = SessionStatus.Connecting;
                Socket.ConnectAsync();                    
            }
            catch
            {
                Disconnect(SessionError.ConnectionError);
            }
        }

        public void Disconnect(object arg)
        {
            Disconnect();
        }

        public void Disconnect(SessionError error = SessionError.None)
        {
            bool bHadSocket = false;

            if (Socket != null)
            {
                bHadSocket = true;

                lock (Socket)
                {
                    Socket = null;
                }
            }

            ResetConnectionState();

            SessionLoginType = SessionLoginType.Unknown;
            AuthenticationFailure = AuthenticationFailure.None;
            SessionStatus = SessionStatus.Disconnected;
            Password = null;
            mTeamPlayerNameMap.Clear();
            Error = error;

            switch (Error)
            {
                case SessionError.None:
                    {
                        if (bHadSocket)
                        {
                            Dispatch.BeginInvoke(() =>
                            {
                                ApplicationModel.Instance.PushMarkdownNotification(Data.Scripting.NotificationType.Error, string.Format("You have been disconnected from the multi-world server."));

                            });
                        }
                    }
                    break;

                case SessionError.RomValidationError:
                    {
                        Dispatch.BeginInvoke(() =>
                        {
                            ApplicationModel.Instance.PushMarkdownNotification(Data.Scripting.NotificationType.Error, string.Format("You were disconnected from the multi-world server because your ROM does not match the server's expectations."));

                        });
                    }
                    break;

                case SessionError.ProtocolError:
                    {
                        Dispatch.BeginInvoke(() =>
                        {
                            ApplicationModel.Instance.PushMarkdownNotification(Data.Scripting.NotificationType.Error, string.Format("You were disconnected from the multi-world server because the server responded to a request in an unexpected way."));

                        });
                    }
                    break;

                default:
                    {
                        Dispatch.BeginInvoke(() =>
                        {
                            ApplicationModel.Instance.PushMarkdownNotification(Data.Scripting.NotificationType.Error, string.Format("You have been disconnected from the multi-world server."));

                        });
                    }
                    break;
            }
        }

#region -- Session Join --

        class LegacyConnectMsg
        {
            public string password { get; set; }
            public string name { get; set; }
            public string team { get; set; }
            public int slot { get; set; }
        }

        class ConnectMsg
        {
            public List<byte> rom { get; set; }
            public string password { get; set; }
        }

        private void JoinSessionCompletionHandler(bool result)
        {
            if (!result)
                Log("Failed to send connect message");
        }

#endregion


        [Command("Print")]
        private void OnPrintCmd(JToken data)
        {
            if (data != null && !string.IsNullOrWhiteSpace(data.ToString()))
                Log(data.ToString());
        }

        [Command("RoomInfo")]
        private void OnRoomInfoCmd(JToken data)
        {
            try
            {
                Description = new SessionDescription(data, this);

                foreach (SessionDescription.Slot slot in Description.Slots)
                {
                    if (UserSlot == null && slot.Available)
                        UserSlot = slot;
                }

                if (Description.Slots.Count == 0)
                    SessionLoginType = SessionLoginType.RomBased;
                else
                    SessionLoginType = SessionLoginType.Legacy;

                SessionStatus = SessionStatus.Connected;

                if (CanAuthenticate())
                    Authenticate();
            }
            catch
            {
                Log("Invalid session description format");
                Disconnect(SessionError.ProtocolError);

                return;
            }
        }

        MemoryTimer mMemoryTimer;

        private void ResetConnectionState()
        {
            ClearMessageLog();
            mReceivedItems.Clear();
            mCheckedLocations.Clear();

            AutoTrackerExtension extension = ExtensionManager.Instance.FindExtension<AutoTrackerExtension>();
            if (extension != null)
            {
                if (mMemoryTimer == null)
                {
                    extension.RemoveMemoryTimer(mMemoryTimer);
                    mMemoryTimer = null;
                }
            }

            AuthenticationFailure = AuthenticationFailure.None;

            UserSlot = null;
            Description = null;
            mExpectedRom = null;
        }

        class ReceivedItem
        {
            public int Item { get; set; }
            public string Location { get; set; }
            public string PlayerName { get; set; }
        }

        List<ReceivedItem> mReceivedItems = new List<ReceivedItem>();

        private IAutoTrackingProvider ActiveProvider
        {
            get
            {
                AutoTrackerExtension autotracker = ExtensionManager.Instance.FindExtension<AutoTrackerExtension>();
                if (autotracker == null || autotracker.ActiveProvider == null || !autotracker.ActiveProvider.IsConnected)
                    return null;

                return autotracker.ActiveProvider;
            }
        }

        private byte[] GetRomHash(IAutoTrackingProvider provider = null)
        {
            provider = provider ?? ActiveProvider;

            if (provider == null)
                return null;

            byte[] romHash = new byte[0x15];
            if (!provider.Read(0x702000, romHash))
                return null;

            return romHash;
        }

        private bool IsRomValid(IAutoTrackingProvider provider = null)
        {
            provider = provider ?? ActiveProvider;

            if (mExpectedRom == null || mExpectedRom.Length > 0x15)
                return false;

            if (ApplicationSettings.Instance.IgnoreBontaMultiWorldRomCheck)
                return true;

            byte[] romHash = GetRomHash();
            if (romHash == null || romHash.Length < mExpectedRom.Length)
                return false;

            for (int i = 0; i < mExpectedRom.Length; ++i)
            {
                if (romHash[i] != mExpectedRom[i])
                    return false;
            }

            return true;
        }

        private bool IsInGame(IAutoTrackingProvider provider = null)
        {
            provider = provider ?? ActiveProvider;

            if (!IsRomValid(provider))
                return false;

            byte gameState;
            if (!provider.Read8(0x7e0010, out gameState))
                return false;

            switch (gameState)
            {
                case 0x07:
                case 0x09:
                case 0x0b:
                    return true;

                default:
                    return false;
            }
        }

        [Command("ItemSent")]
        private void OnItemSent(JToken data)
        {
            JArray container = data as JArray;
            if (container != null && container.Count == 4)
            {
                try
                {
                    string userFrom = "Unknown Player";
                    string userTo = "Unknown Player";
                    int itemCode = -1;
                    int locationCode = -1;

                    if (SessionLoginType == SessionLoginType.Legacy)
                    {
                        userFrom = container[0].Value<string>();
                        userTo = container[1].Value<string>();
                        itemCode = container[2].Value<int>();
                        locationCode = container[3].Value<int>();
                    }
                    else
                    {
                        int fromPlayerIdx = container[0].Value<int>();
                        int toPlayerIdx = container[2].Value<int>();

                        userFrom = GetPlayerNameForIndex(fromPlayerIdx);
                        userTo = GetPlayerNameForIndex(toPlayerIdx);
                        locationCode = container[1].Value<int>();
                        itemCode = container[3].Value<int>();
                    }

                    Log("{0} sent {1} {2} ({3})", userFrom, userTo, GetItemNameForID(itemCode), GetLocationNameForID(locationCode));

                    var notificationLevel = ApplicationSettings.Instance.MultiworldNotificationLevel;
                    if ((notificationLevel >= MultiworldNotificationLevel.Verbose && string.Equals(userFrom, mUserName)) ||
                        (notificationLevel >= MultiworldNotificationLevel.Verbose && string.Equals(userTo, mUserName)))
                    {
                        Dispatch.BeginInvoke(() =>
                        {
                            ApplicationModel.Instance.PushMarkdownNotification(Data.Scripting.NotificationType.Message, string.Format("**{0}** sent **{1}** {2} *({3})*", userFrom, userTo, GetItemNameForID(itemCode), GetLocationNameForID(locationCode)));

                        });
                    }
                }
                catch
                {
                }
            }
        }

        private string GetPlayerNameForIndex(int idx)
        {
            string playerName;
            if (!mTeamPlayerNameMap.TryGetValue(idx, out playerName))
                playerName = "Unknown Player";

            return playerName;
        }

        [Command("ReceivedItems")]
        private void OnItemsReceived(JToken data)
        {
            lock (mReceivedItems)
            {
                JArray container = (JArray)data;

                int start_idx = container[0].Value<int>();
                JArray items = container[1].Value<JArray>();

                if (start_idx == 0)
                {
                    mReceivedItems.Clear();
                }
                else if (start_idx != mReceivedItems.Count)
                {
                    mReceivedItems.Clear();
                    SendResync();
                    return;
                }

                if (start_idx == mReceivedItems.Count)
                {
                    //  Items are encoded as arrays for some reason :shrug:
                    foreach (JArray item in items)
                    {
                        ReceivedItem instance;

                        if (SessionLoginType == SessionLoginType.Legacy)
                        {
                            instance = new ReceivedItem()
                            {
                                Item = item[0].Value<int>(),
                                PlayerName = item[3].Value<string>()
                            };

                            try
                            {
                                instance.Location = GetLocationNameForID(item[1].Value<int>());
                            }
                            catch
                            {
                                instance.Location = item[1].Value<string>();
                            }
                        }
                        else
                        {
                            instance = new ReceivedItem()
                            {
                                Item = item[0].Value<int>(),
                                PlayerName = GetPlayerNameForIndex(item[2].Value<int>())
                            };

                            try
                            {
                                instance.Location = GetLocationNameForID(item[1].Value<int>());
                            }
                            catch
                            {
                                instance.Location = item[1].Value<string>();
                            }
                        }

                        mReceivedItems.Add(instance);
                    }
                }
            }
        }

        private bool WriteReceivedItems(IAutoTrackingProvider provider, PackageManager.Game game)
        {
            const ulong RECV_PROGRESS_ADDR = 0x7ef4d0;
            const ulong RECV_ITEM_ADDR = 0x7ef4d2;

            if (!IsInGame(provider))
                return true;

            try
            {
                lock (mReceivedItems)
                {
                    ushort recv_idx = 0;
                    if (provider.Read16(RECV_PROGRESS_ADDR, out recv_idx) && recv_idx < mReceivedItems.Count)
                    {
                        byte pendingItemCode = 0;
                        if (provider.Read8(RECV_ITEM_ADDR, out pendingItemCode) && pendingItemCode == 0)
                        {
                            ReceivedItem item = mReceivedItems[(int)recv_idx];

                            if (ApplicationSettings.Instance.MultiworldNotificationLevel >= MultiworldNotificationLevel.Normal)
                            {
                                Dispatch.BeginInvoke(() =>
                                {
                                    Log("Received {0} from {1} ({2})", GetItemNameForID(item.Item), item.PlayerName, item.Location);
                                    ApplicationModel.Instance.PushMarkdownNotification(Data.Scripting.NotificationType.Celebration, string.Format("Received **{0}** from **{1}** *({2})*", GetItemNameForID(item.Item), item.PlayerName, item.Location));

                                });
                            }

                            provider.Write16(RECV_PROGRESS_ADDR, ++recv_idx);
                            provider.Write8(RECV_ITEM_ADDR, (byte)item.Item);
                        }
                    }
                }

                return true;
            }
            catch
            {
            }

            return false;
        }

        HashSet<LocationData> mCheckedLocations = new HashSet<LocationData>();

        private void MemorySegment_OnMemorySegmentUpdated(MemorySegment segment, IAutoTrackingProvider provider, PackageManager.Game game)
        {
            List<LocationData> newChecks = new List<LocationData>();

            bool bHasCheckedInGameState = false;
            foreach (LocationData location in Locations)
            {
                if (mCheckedLocations.Contains(location))
                    continue;

                ulong locationAddress = 0x7ef000 + location.Offset;

                if (segment.ContainsAddress(locationAddress))
                {
#region -- Verify In-Game --
                    if (!bHasCheckedInGameState)
                    {
                        if (!IsInGame(provider))
                            return;

                        bHasCheckedInGameState = true;
                    }
                    #endregion

                    byte locationFlags = segment.ReadUInt8(locationAddress);
                    if ((locationFlags & location.Mask) != 0)
                    {
                        newChecks.Add(location);
                        mCheckedLocations.Add(location);
                    }
                }
            }

            SendLocationChecks(newChecks);
        }

        private Message BuildLocationChecksMessage(IEnumerable<LocationData> locations)
        {
            if (locations == null)
                return null;

            if (!locations.Any())
                return null;

            JArray locationIDArray = new JArray();
            foreach (LocationData location in locations)
            {
                locationIDArray.Add(JToken.FromObject(location.ID));
            }

            return new Message()
            {
                Command = "LocationChecks",
                Data = locationIDArray
            };
        }

        private void SendLocationChecks(IEnumerable<LocationData> locations)
        {
            SendMessages((x) => { }, BuildLocationChecksMessage(locations));
        }

        private void SendResync()
        {
            SendMessages((x) => { }, new Message()
            {
                Command = "Sync"
            },
            BuildLocationChecksMessage(mCheckedLocations));
        }

        public void Say(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                SendMessages((x) => { }, new Message()
                {
                    Command = "Say",
                    Data = JToken.FromObject(text)
                });
            }
        }

#region -- Location Data --

        struct LocationData
        {
            public LocationData(string name, ulong offset, byte mask, int id)
            {
                Name = name;
                Offset = offset;
                Mask = mask;
                ID = id;
            }

            public string Name;
            public ulong Offset;
            public byte Mask;
            public int ID;
        }

        public string GetLocationNameForID(int id)
        {
            if (id == -100)
                return "Cheat Console";

            foreach (LocationData l in Locations)
            {
                if (l.ID == id)
                    return l.Name;
            }

            return "Unknown Location";
        }

        static LocationData[] Locations =
        {
            new LocationData("Mushroom", 0x411, 0x10, 0x180013),
            new LocationData("Bottle Merchant", 0x3c9, 0x2, 0x2eb18),
            new LocationData("Flute Spot", 0x2aa, 0x40, 0x18014a),
            new LocationData("Sunken Treasure", 0x2bb, 0x40, 0x180145),
            new LocationData("Purple Chest", 0x3c9, 0x10, 0x33d68),
            new LocationData("Blind's Hideout - Top", 0x23a, 0x10, 0xeb0f),
            new LocationData("Blind's Hideout - Left", 0x23a, 0x20, 0xeb12),
            new LocationData("Blind's Hideout - Right", 0x23a, 0x40, 0xeb15),
            new LocationData("Blind's Hideout - Far Left", 0x23a, 0x80, 0xeb18),
            new LocationData("Blind's Hideout - Far Right", 0x23b, 0x1, 0xeb1b),
            new LocationData("Link's Uncle", 0x3c6, 0x1, 0x2df45),
            new LocationData("Secret Passage", 0xaa, 0x10, 0xe971),
            new LocationData("King Zora", 0x410, 0x2, 0xee1c3),
            new LocationData("Zora's Ledge", 0x301, 0x40, 0x180149),
            new LocationData("Waterfall Fairy - Left", 0x228, 0x10, 0xe9b0),
            new LocationData("Waterfall Fairy - Right", 0x228, 0x20, 0xe9d1),
            new LocationData("King's Tomb", 0x226, 0x10, 0xe97a),
            new LocationData("Floodgate Chest", 0x216, 0x10, 0xe98c),
            new LocationData("Link's House", 0x208, 0x10, 0xe9bc),
            new LocationData("Kakariko Tavern", 0x206, 0x10, 0xe9ce),
            new LocationData("Chicken House", 0x210, 0x10, 0xe9e9),
            new LocationData("Aginah's Cave", 0x214, 0x10, 0xe9f2),
            new LocationData("Sahasrahla's Hut - Left", 0x20a, 0x10, 0xea82),
            new LocationData("Sahasrahla's Hut - Middle", 0x20a, 0x20, 0xea85),
            new LocationData("Sahasrahla's Hut - Right", 0x20a, 0x40, 0xea88),
            new LocationData("Sahasrahla", 0x410, 0x10, 0x2f1fc),
            new LocationData("Kakariko Well - Top", 0x5e, 0x10, 0xea8e),
            new LocationData("Kakariko Well - Left", 0x5e, 0x20, 0xea91),
            new LocationData("Kakariko Well - Middle", 0x5e, 0x40, 0xea94),
            new LocationData("Kakariko Well - Right", 0x5e, 0x80, 0xea97),
            new LocationData("Kakariko Well - Bottom", 0x5f, 0x1, 0xea9a),
            new LocationData("Blacksmith", 0x411, 0x4, 0x18002a),
            new LocationData("Magic Bat", 0x411, 0x80, 0x180015),
            new LocationData("Sick Kid", 0x410, 0x4, 0x339cf),
            new LocationData("Hobo", 0x3c9, 0x1, 0x33e7d),
            new LocationData("Lost Woods Hideout", 0x1c3, 0x2, 0x180000),
            new LocationData("Lumberjack Tree", 0x1c5, 0x2, 0x180001),
            new LocationData("Cave 45", 0x237, 0x4, 0x180003),
            new LocationData("Graveyard Cave", 0x237, 0x2, 0x180004),
            new LocationData("Checkerboard Cave", 0x24d, 0x2, 0x180005),
            new LocationData("Mini Moldorm Cave - Far Left", 0x246, 0x10, 0xeb42),
            new LocationData("Mini Moldorm Cave - Left", 0x246, 0x20, 0xeb45),
            new LocationData("Mini Moldorm Cave - Right", 0x246, 0x40, 0xeb48),
            new LocationData("Mini Moldorm Cave - Far Right", 0x246, 0x80, 0xeb4b),
            new LocationData("Mini Moldorm Cave - Generous Guy", 0x247, 0x4, 0x180010),
            new LocationData("Ice Rod Cave", 0x240, 0x10, 0xeb4e),
            new LocationData("Bonk Rock Cave", 0x248, 0x10, 0xeb3f),
            new LocationData("Library", 0x410, 0x80, 0x180012),
            new LocationData("Potion Shop", 0x411, 0x20, 0x180014),
            new LocationData("Lake Hylia Island", 0x2b5, 0x40, 0x180144),
            new LocationData("Maze Race", 0x2a8, 0x40, 0x180142),
            new LocationData("Desert Ledge", 0x2b0, 0x40, 0x180143),
            new LocationData("Desert Palace - Big Chest", 0xe6, 0x10, 0xe98f),
            new LocationData("Desert Palace - Torch", 0xe7, 0x4, 0x180160),
            new LocationData("Desert Palace - Map Chest", 0xe8, 0x10, 0xe9b6),
            new LocationData("Desert Palace - Compass Chest", 0x10a, 0x10, 0xe9cb),
            new LocationData("Desert Palace - Big Key Chest", 0xea, 0x10, 0xe9c2),
            new LocationData("Desert Palace - Boss", 0x67, 0x8, 0x180151),
            new LocationData("Eastern Palace - Compass Chest", 0x150, 0x10, 0xe977),
            new LocationData("Eastern Palace - Big Chest", 0x152, 0x10, 0xe97d),
            new LocationData("Eastern Palace - Cannonball Chest", 0x172, 0x10, 0xe9b3),
            new LocationData("Eastern Palace - Big Key Chest", 0x170, 0x10, 0xe9b9),
            new LocationData("Eastern Palace - Map Chest", 0x154, 0x10, 0xe9f5),
            new LocationData("Eastern Palace - Boss", 0x191, 0x8, 0x180150),
            new LocationData("Master Sword Pedestal", 0x300, 0x40, 0x289b0),
            new LocationData("Hyrule Castle - Boomerang Chest", 0xe2, 0x10, 0xe974),
            new LocationData("Hyrule Castle - Map Chest", 0xe4, 0x10, 0xeb0c),
            new LocationData("Hyrule Castle - Zelda's Chest", 0x100, 0x10, 0xeb09),
            new LocationData("Sewers - Dark Cross", 0x64, 0x10, 0xe96e),
            new LocationData("Sewers - Secret Room - Left", 0x22, 0x10, 0xeb5d),
            new LocationData("Sewers - Secret Room - Middle", 0x22, 0x20, 0xeb60),
            new LocationData("Sewers - Secret Room - Right", 0x22, 0x40, 0xeb63),
            new LocationData("Sanctuary", 0x24, 0x10, 0xea79),
            new LocationData("Castle Tower - Room 03", 0x1c0, 0x10, 0xeab5),
            new LocationData("Castle Tower - Dark Maze", 0x1a0, 0x10, 0xeab2),
            new LocationData("Old Man", 0x410, 0x1, 0xf69fa),
            new LocationData("Spectacle Rock Cave", 0x1d5, 0x4, 0x180002),
            new LocationData("Paradox Cave Lower - Far Left", 0x1de, 0x10, 0xeb2a),
            new LocationData("Paradox Cave Lower - Left", 0x1de, 0x20, 0xeb2d),
            new LocationData("Paradox Cave Lower - Right", 0x1de, 0x40, 0xeb30),
            new LocationData("Paradox Cave Lower - Far Right", 0x1de, 0x80, 0xeb33),
            new LocationData("Paradox Cave Lower - Middle", 0x1df, 0x1, 0xeb36),
            new LocationData("Paradox Cave Upper - Left", 0x1fe, 0x10, 0xeb39),
            new LocationData("Paradox Cave Upper - Right", 0x1fe, 0x20, 0xeb3c),
            new LocationData("Spiral Cave", 0x1fc, 0x10, 0xe9bf),
            new LocationData("Ether Tablet", 0x411, 0x1, 0x180016),
            new LocationData("Spectacle Rock", 0x283, 0x40, 0x180140),
            new LocationData("Tower of Hera - Basement Cage", 0x10f, 0x4, 0x180162),
            new LocationData("Tower of Hera - Map Chest", 0xee, 0x10, 0xe9ad),
            new LocationData("Tower of Hera - Big Key Chest", 0x10e, 0x10, 0xe9e6),
            new LocationData("Tower of Hera - Compass Chest", 0x4e, 0x20, 0xe9fb),
            new LocationData("Tower of Hera - Big Chest", 0x4e, 0x10, 0xe9f8),
            new LocationData("Tower of Hera - Boss", 0xf, 0x8, 0x180152),
            new LocationData("Pyramid", 0x2db, 0x40, 0x180147),
            new LocationData("Catfish", 0x410, 0x20, 0xee185),
            new LocationData("Stumpy", 0x410, 0x8, 0x330c7),
            new LocationData("Digging Game", 0x2e8, 0x40, 0x180148),
            new LocationData("Bombos Tablet", 0x411, 0x2, 0x180017),
            new LocationData("Hype Cave - Top", 0x23c, 0x10, 0xeb1e),
            new LocationData("Hype Cave - Middle Right", 0x23c, 0x20, 0xeb21),
            new LocationData("Hype Cave - Middle Left", 0x23c, 0x40, 0xeb24),
            new LocationData("Hype Cave - Bottom", 0x23c, 0x80, 0xeb27),
            new LocationData("Hype Cave - Generous Guy", 0x23d, 0x4, 0x180011),
            new LocationData("Peg Cave", 0x24f, 0x4, 0x180006),
            new LocationData("Pyramid Fairy - Left", 0x22c, 0x10, 0xe980),
            new LocationData("Pyramid Fairy - Right", 0x22c, 0x20, 0xe983),
            new LocationData("Brewery", 0x20c, 0x10, 0xe9ec),
            new LocationData("C-Shaped House", 0x238, 0x10, 0xe9ef),
            new LocationData("Chest Game", 0x20d, 0x4, 0xeda8),
            new LocationData("Bumper Cave Ledge", 0x2ca, 0x40, 0x180146),
            new LocationData("Mire Shed - Left", 0x21a, 0x10, 0xea73),
            new LocationData("Mire Shed - Right", 0x21a, 0x20, 0xea76),
            new LocationData("Superbunny Cave - Top", 0x1f0, 0x10, 0xea7c),
            new LocationData("Superbunny Cave - Bottom", 0x1f0, 0x20, 0xea7f),
            new LocationData("Spike Cave", 0x22e, 0x10, 0xea8b),
            new LocationData("Hookshot Cave - Top Right", 0x78, 0x10, 0xeb51),
            new LocationData("Hookshot Cave - Top Left", 0x78, 0x20, 0xeb54),
            new LocationData("Hookshot Cave - Bottom Right", 0x78, 0x80, 0xeb5a),
            new LocationData("Hookshot Cave - Bottom Left", 0x78, 0x40, 0xeb57),
            new LocationData("Floating Island", 0x285, 0x40, 0x180141),
            new LocationData("Mimic Cave", 0x218, 0x10, 0xe9c5),
            new LocationData("Swamp Palace - Entrance", 0x50, 0x10, 0xea9d),
            new LocationData("Swamp Palace - Map Chest", 0x6e, 0x10, 0xe986),
            new LocationData("Swamp Palace - Big Chest", 0x6c, 0x10, 0xe989),
            new LocationData("Swamp Palace - Compass Chest", 0x8c, 0x10, 0xeaa0),
            new LocationData("Swamp Palace - Big Key Chest", 0x6a, 0x10, 0xeaa6),
            new LocationData("Swamp Palace - West Chest", 0x68, 0x10, 0xeaa3),
            new LocationData("Swamp Palace - Flooded Room - Left", 0xec, 0x10, 0xeaa9),
            new LocationData("Swamp Palace - Flooded Room - Right", 0xec, 0x20, 0xeaac),
            new LocationData("Swamp Palace - Waterfall Room", 0xcc, 0x10, 0xeaaf),
            new LocationData("Swamp Palace - Boss", 0xd, 0x8, 0x180154),
            new LocationData("Thieves' Town - Big Key Chest", 0x1b6, 0x20, 0xea04),
            new LocationData("Thieves' Town - Map Chest", 0x1b6, 0x10, 0xea01),
            new LocationData("Thieves' Town - Compass Chest", 0x1b8, 0x10, 0xea07),
            new LocationData("Thieves' Town - Ambush Chest", 0x196, 0x10, 0xea0a),
            new LocationData("Thieves' Town - Attic", 0xca, 0x10, 0xea0d),
            new LocationData("Thieves' Town - Big Chest", 0x88, 0x10, 0xea10),
            new LocationData("Thieves' Town - Blind's Cell", 0x8a, 0x10, 0xea13),
            new LocationData("Thieves' Town - Boss", 0x159, 0x8, 0x180156),
            new LocationData("Skull Woods - Compass Chest", 0xce, 0x10, 0xe992),
            new LocationData("Skull Woods - Map Chest", 0xb0, 0x20, 0xe99b),
            new LocationData("Skull Woods - Big Chest", 0xb0, 0x10, 0xe998),
            new LocationData("Skull Woods - Pot Prison", 0xae, 0x20, 0xe9a1),
            new LocationData("Skull Woods - Pinball Room", 0xd0, 0x10, 0xe9c8),
            new LocationData("Skull Woods - Big Key Chest", 0xae, 0x10, 0xe99e),
            new LocationData("Skull Woods - Bridge Room", 0xb2, 0x10, 0xe9fe),
            new LocationData("Skull Woods - Boss", 0x53, 0x8, 0x180155),
            new LocationData("Ice Palace - Compass Chest", 0x5c, 0x10, 0xe9d4),
            new LocationData("Ice Palace - Freezor Chest", 0xfc, 0x10, 0xe995),
            new LocationData("Ice Palace - Big Chest", 0x13c, 0x10, 0xe9aa),
            new LocationData("Ice Palace - Iced T Room", 0x15c, 0x10, 0xe9e3),
            new LocationData("Ice Palace - Spike Room", 0xbe, 0x10, 0xe9e0),
            new LocationData("Ice Palace - Big Key Chest", 0x3e, 0x10, 0xe9a4),
            new LocationData("Ice Palace - Map Chest", 0x7e, 0x10, 0xe9dd),
            new LocationData("Ice Palace - Boss", 0x1bd, 0x8, 0x180157),
            new LocationData("Misery Mire - Big Chest", 0x186, 0x10, 0xea67),
            new LocationData("Misery Mire - Map Chest", 0x186, 0x20, 0xea6a),
            new LocationData("Misery Mire - Main Lobby", 0x184, 0x10, 0xea5e),
            new LocationData("Misery Mire - Bridge Chest", 0x144, 0x10, 0xea61),
            new LocationData("Misery Mire - Spike Chest", 0x166, 0x10, 0xe9da),
            new LocationData("Misery Mire - Compass Chest", 0x182, 0x10, 0xea64),
            new LocationData("Misery Mire - Big Key Chest", 0x1a2, 0x10, 0xea6d),
            new LocationData("Misery Mire - Boss", 0x121, 0x8, 0x180158),
            new LocationData("Turtle Rock - Compass Chest", 0x1ac, 0x10, 0xea22),
            new LocationData("Turtle Rock - Roller Room - Left", 0x16e, 0x10, 0xea1c),
            new LocationData("Turtle Rock - Roller Room - Right", 0x16e, 0x20, 0xea1f),
            new LocationData("Turtle Rock - Chain Chomps", 0x16c, 0x10, 0xea16),
            new LocationData("Turtle Rock - Big Key Chest", 0x28, 0x10, 0xea25),
            new LocationData("Turtle Rock - Big Chest", 0x48, 0x10, 0xea19),
            new LocationData("Turtle Rock - Crystaroller Room", 0x8, 0x10, 0xea34),
            new LocationData("Turtle Rock - Eye Bridge - Bottom Left", 0x1aa, 0x80, 0xea31),
            new LocationData("Turtle Rock - Eye Bridge - Bottom Right", 0x1aa, 0x40, 0xea2e),
            new LocationData("Turtle Rock - Eye Bridge - Top Left", 0x1aa, 0x20, 0xea2b),
            new LocationData("Turtle Rock - Eye Bridge - Top Right", 0x1aa, 0x10, 0xea28),
            new LocationData("Turtle Rock - Boss", 0x149, 0x8, 0x180159),
            new LocationData("Palace of Darkness - Shooter Room", 0x12, 0x10, 0xea5b),
            new LocationData("Palace of Darkness - The Arena - Bridge", 0x54, 0x20, 0xea3d),
            new LocationData("Palace of Darkness - Stalfos Basement", 0x14, 0x10, 0xea49),
            new LocationData("Palace of Darkness - Big Key Chest", 0x74, 0x10, 0xea37),
            new LocationData("Palace of Darkness - The Arena - Ledge", 0x54, 0x10, 0xea3a),
            new LocationData("Palace of Darkness - Map Chest", 0x56, 0x10, 0xea52),
            new LocationData("Palace of Darkness - Compass Chest", 0x34, 0x20, 0xea43),
            new LocationData("Palace of Darkness - Dark Basement - Left", 0xd4, 0x10, 0xea4c),
            new LocationData("Palace of Darkness - Dark Basement - Right", 0xd4, 0x20, 0xea4f),
            new LocationData("Palace of Darkness - Dark Maze - Top", 0x32, 0x10, 0xea55),
            new LocationData("Palace of Darkness - Dark Maze - Bottom", 0x32, 0x20, 0xea58),
            new LocationData("Palace of Darkness - Big Chest", 0x34, 0x10, 0xea40),
            new LocationData("Palace of Darkness - Harmless Hellway", 0x34, 0x40, 0xea46),
            new LocationData("Palace of Darkness - Boss", 0xb5, 0x8, 0x180153),
            new LocationData("Ganons Tower - Bob's Torch", 0x119, 0x4, 0x180161),
            new LocationData("Ganons Tower - Hope Room - Left", 0x118, 0x20, 0xead9),
            new LocationData("Ganons Tower - Hope Room - Right", 0x118, 0x40, 0xeadc),
            new LocationData("Ganons Tower - Tile Room", 0x11a, 0x10, 0xeae2),
            new LocationData("Ganons Tower - Compass Room - Top Left", 0x13a, 0x10, 0xeae5),
            new LocationData("Ganons Tower - Compass Room - Top Right", 0x13a, 0x20, 0xeae8),
            new LocationData("Ganons Tower - Compass Room - Bottom Left", 0x13a, 0x40, 0xeaeb),
            new LocationData("Ganons Tower - Compass Room - Bottom Right", 0x13a, 0x80, 0xeaee),
            new LocationData("Ganons Tower - DMs Room - Top Left", 0xf6, 0x10, 0xeab8),
            new LocationData("Ganons Tower - DMs Room - Top Right", 0xf6, 0x20, 0xeabb),
            new LocationData("Ganons Tower - DMs Room - Bottom Left", 0xf6, 0x40, 0xeabe),
            new LocationData("Ganons Tower - DMs Room - Bottom Right", 0xf6, 0x80, 0xeac1),
            new LocationData("Ganons Tower - Map Chest", 0x116, 0x10, 0xead3),
            new LocationData("Ganons Tower - Firesnake Room", 0xfa, 0x10, 0xead0),
            new LocationData("Ganons Tower - Randomizer Room - Top Left", 0xf8, 0x10, 0xeac4),
            new LocationData("Ganons Tower - Randomizer Room - Top Right", 0xf8, 0x20, 0xeac7),
            new LocationData("Ganons Tower - Randomizer Room - Bottom Left", 0xf8, 0x40, 0xeaca),
            new LocationData("Ganons Tower - Randomizer Room - Bottom Right", 0xf8, 0x80, 0xeacd),
            new LocationData("Ganons Tower - Bob's Chest", 0x118, 0x80, 0xeadf),
            new LocationData("Ganons Tower - Big Chest", 0x118, 0x10, 0xead6),
            new LocationData("Ganons Tower - Big Key Room - Left", 0x38, 0x20, 0xeaf4),
            new LocationData("Ganons Tower - Big Key Room - Right", 0x38, 0x40, 0xeaf7),
            new LocationData("Ganons Tower - Big Key Chest", 0x38, 0x10, 0xeaf1),
            new LocationData("Ganons Tower - Mini Helmasaur Room - Left", 0x7a, 0x10, 0xeafd),
            new LocationData("Ganons Tower - Mini Helmasaur Room - Right", 0x7a, 0x20, 0xeb00),
            new LocationData("Ganons Tower - Pre-Moldorm Chest", 0x7a, 0x40, 0xeb03),
            new LocationData("Ganons Tower - Validation Chest", 0x9a, 0x10, 0xeb06)
        };

#endregion

#region -- Item Descriptions --

        struct ItemDescription
        {
            public ItemDescription(string name, int id)
            {
                Name = name;
                ID = id;
            }

            public string Name;
            public int ID;
        }

        public string GetItemNameForID(int id)
        {
            foreach (ItemDescription item in Items)
            {
                if (item.ID == id)
                    return item.Name;
            }

            return "Unknown Item";
        }

        static ItemDescription[] Items =
        {
            new ItemDescription("Bow", 11),
            new ItemDescription("Progressive Bow", 100),
            new ItemDescription("Progressive Bow", 101),
            new ItemDescription("Book of Mudora", 29),
            new ItemDescription("Hammer", 9),
            new ItemDescription("Hookshot", 10),
            new ItemDescription("Magic Mirror", 26),
            new ItemDescription("Ocarina", 20),
            new ItemDescription("Pegasus Boots", 75),
            new ItemDescription("Power Glove", 27),
            new ItemDescription("Cape", 25),
            new ItemDescription("Mushroom", 41),
            new ItemDescription("Shovel", 19),
            new ItemDescription("Lamp", 18),
            new ItemDescription("Magic Powder", 13),
            new ItemDescription("Moon Pearl", 31),
            new ItemDescription("Cane of Somaria", 21),
            new ItemDescription("Fire Rod", 7),
            new ItemDescription("Flippers", 30),
            new ItemDescription("Ice Rod", 8),
            new ItemDescription("Titans Mitts", 28),
            new ItemDescription("Ether", 16),
            new ItemDescription("Bombos", 15),
            new ItemDescription("Quake", 17),
            new ItemDescription("Bottle", 22),
            new ItemDescription("Bottle (Red Potion)", 43),
            new ItemDescription("Bottle (Green Potion)", 44),
            new ItemDescription("Bottle (Blue Potion)", 45),
            new ItemDescription("Bottle (Fairy)", 61),
            new ItemDescription("Bottle (Bee)", 60),
            new ItemDescription("Bottle (Good Bee)", 72),
            new ItemDescription("Master Sword", 80),
            new ItemDescription("Tempered Sword", 2),
            new ItemDescription("Fighter Sword", 73),
            new ItemDescription("Golden Sword", 3),
            new ItemDescription("Progressive Sword", 94),
            new ItemDescription("Progressive Glove", 97),
            new ItemDescription("Silver Arrows", 88),
            new ItemDescription("Triforce", 106),
            new ItemDescription("Power Star", 107),
            new ItemDescription("Triforce Piece", 108),
            new ItemDescription("Single Arrow", 67),
            new ItemDescription("Arrows (10)", 68),
            new ItemDescription("Arrow Upgrade (+10)", 84),
            new ItemDescription("Arrow Upgrade (+5)", 83),
            new ItemDescription("Single Bomb", 39),
            new ItemDescription("Bombs (3)", 40),
            new ItemDescription("Bombs (10)", 49),
            new ItemDescription("Bomb Upgrade (+10)", 82),
            new ItemDescription("Bomb Upgrade (+5)", 81),
            new ItemDescription("Blue Mail", 34),
            new ItemDescription("Red Mail", 35),
            new ItemDescription("Progressive Armor", 96),
            new ItemDescription("Blue Boomerang", 12),
            new ItemDescription("Red Boomerang", 42),
            new ItemDescription("Blue Shield", 4),
            new ItemDescription("Red Shield", 5),
            new ItemDescription("Mirror Shield", 6),
            new ItemDescription("Progressive Shield", 95),
            new ItemDescription("Bug Catching Net", 33),
            new ItemDescription("Cane of Byrna", 24),
            new ItemDescription("Boss Heart Container", 62),
            new ItemDescription("Sanctuary Heart Container", 63),
            new ItemDescription("Piece of Heart", 23),
            new ItemDescription("Rupee (1)", 52),
            new ItemDescription("Rupees (5)", 53),
            new ItemDescription("Rupees (20)", 54),
            new ItemDescription("Rupees (50)", 65),
            new ItemDescription("Rupees (100)", 64),
            new ItemDescription("Rupees (300)", 70),
            new ItemDescription("Rupoor", 89),
            new ItemDescription("Red Clock", 91),
            new ItemDescription("Blue Clock", 92),
            new ItemDescription("Green Clock", 93),
            new ItemDescription("Single RNG", 98),
            new ItemDescription("Multi RNG", 99),
            new ItemDescription("Magic Upgrade (1/2)", 78),
            new ItemDescription("Magic Upgrade (1/4)", 79),
            new ItemDescription("Small Key (Eastern Palace)", 162),
            new ItemDescription("Big Key (Eastern Palace)", 157),
            new ItemDescription("Compass (Eastern Palace)", 141),
            new ItemDescription("Map (Eastern Palace)", 125),
            new ItemDescription("Small Key (Desert Palace)", 163),
            new ItemDescription("Big Key (Desert Palace)", 156),
            new ItemDescription("Compass (Desert Palace)", 140),
            new ItemDescription("Map (Desert Palace)", 124),
            new ItemDescription("Small Key (Tower of Hera)", 170),
            new ItemDescription("Big Key (Tower of Hera)", 149),
            new ItemDescription("Compass (Tower of Hera)", 133),
            new ItemDescription("Map (Tower of Hera)", 117),
            new ItemDescription("Small Key (Escape)", 160),
            new ItemDescription("Big Key (Escape)", 159),
            new ItemDescription("Compass (Escape)", 143),
            new ItemDescription("Map (Escape)", 127),
            new ItemDescription("Small Key (Agahnims Tower)", 164),
            new ItemDescription("Small Key (Palace of Darkness)", 166),
            new ItemDescription("Big Key (Palace of Darkness)", 153),
            new ItemDescription("Compass (Palace of Darkness)", 137),
            new ItemDescription("Map (Palace of Darkness)", 121),
            new ItemDescription("Small Key (Thieves Town)", 171),
            new ItemDescription("Big Key (Thieves Town)", 148),
            new ItemDescription("Compass (Thieves Town)", 132),
            new ItemDescription("Map (Thieves Town)", 116),
            new ItemDescription("Small Key (Skull Woods)", 168),
            new ItemDescription("Big Key (Skull Woods)", 151),
            new ItemDescription("Compass (Skull Woods)", 135),
            new ItemDescription("Map (Skull Woods)", 119),
            new ItemDescription("Small Key (Swamp Palace)", 165),
            new ItemDescription("Big Key (Swamp Palace)", 154),
            new ItemDescription("Compass (Swamp Palace)", 138),
            new ItemDescription("Map (Swamp Palace)", 122),
            new ItemDescription("Small Key (Ice Palace)", 169),
            new ItemDescription("Big Key (Ice Palace)", 150),
            new ItemDescription("Compass (Ice Palace)", 134),
            new ItemDescription("Map (Ice Palace)", 118),
            new ItemDescription("Small Key (Misery Mire)", 167),
            new ItemDescription("Big Key (Misery Mire)", 152),
            new ItemDescription("Compass (Misery Mire)", 136),
            new ItemDescription("Map (Misery Mire)", 120),
            new ItemDescription("Small Key (Turtle Rock)", 172),
            new ItemDescription("Big Key (Turtle Rock)", 147),
            new ItemDescription("Compass (Turtle Rock)", 131),
            new ItemDescription("Map (Turtle Rock)", 115),
            new ItemDescription("Small Key (Ganons Tower)", 173),
            new ItemDescription("Big Key (Ganons Tower)", 146),
            new ItemDescription("Compass (Ganons Tower)", 130),
            new ItemDescription("Map (Ganons Tower)", 114),
            new ItemDescription("Small Key (Universal)", 175),
            new ItemDescription("Nothing", 90),
            new ItemDescription("Red Potion", 46),
            new ItemDescription("Green Potion", 47),
            new ItemDescription("Blue Potion", 48),
            new ItemDescription("Bee", 14),
            new ItemDescription("Small Heart", 66)
        };

#endregion

    }
}
