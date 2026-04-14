using EmoTracker.Core;
using EmoTracker.Core.Services;
using EmoTracker.Data;
using EmoTracker.Data.Items;
using EmoTracker.Data.JSON;
using EmoTracker.UI.Media.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Events;
using EmoTracker.Data.Session;

namespace EmoTracker.Extensions.Twitch
{

    [Flags]
    public enum DefaultPermissions
    {
        None = 0,
        Moderator = 0x1,
        Subscriber = 0x2,
        VIP = 0x4,
        All = 0x8000
    }

    public enum ConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    public class TwitchExtension : ObservableObject, Extension
   {
        enum DisconnectReason
        {
            Unknown,
            Reset,
            User,
            Error
        }

        public DelegateCommand ConnectCommand { get; private set; }
        public DelegateCommand DisconnectCommand { get; private set; }

        System.DateTime mLastVFXCommandTime = System.DateTime.MinValue;
        DefaultPermissions mDefaultPermissions = DefaultPermissions.Moderator;
        ConnectionState mConnectionState = ConnectionState.Disconnected;
        DisconnectReason mDisconnectReason = DisconnectReason.Unknown;
        object mStatusControl;
        TwitchClient mClient;
        bool mbActive = false;

        List<string> mUserBlacklist = new List<string>();
        List<string> mUserWhitelist = new List<string>();

        public ConnectionState ConnectionState
        {
            get { return mConnectionState; }
            set
            {
                if (SetProperty(ref mConnectionState, value))
                {
                    Dispatch.BeginInvoke(() =>
                    {
                        ConnectCommand.RaiseCanExecuteChanged();
                        DisconnectCommand.RaiseCanExecuteChanged();
                    });
                }
            }
        }

        public string Name { get { return "Twitch Chat HUD"; } }

        public string UID { get { return "twitch_chat_hud"; } }

        public int Priority { get { return 0; } }

        public object StatusBarControl
        {
            get
            {
                return mbActive ? mStatusControl : null;
            }
        }

        public void Start()
        {
            if (!string.IsNullOrWhiteSpace(TrackerSession.Current.Global.TwitchChannelName))
            {
                mbActive = true;
                LoadPermissions();
            }
        }

        public void Stop()
        {
            Disconnect(DisconnectReason.Reset);
        }

        public void OnPackageUnloaded()
        {
        }

        public void OnPackageLoaded()
        {
        }

        public JToken SerializeToJson()
        {
            return null;
        }

        public bool DeserializeFromJson(JToken token)
        {
            return true;
        }

        public TwitchExtension()
        {
            ConnectCommand = new DelegateCommand(ConnectExecute, ConnectCanExecute);
            DisconnectCommand = new DelegateCommand(DisconnectExecute, DisconnectCanExecute);

            mStatusControl = new TwitchStatusIndicator() { DataContext = this };
        }

        private bool DisconnectCanExecute(object obj)
        {
            return ConnectionState == ConnectionState.Connected;
        }

        private void DisconnectExecute(object obj)
        {
            Disconnect(DisconnectReason.User);
        }

        private bool ConnectCanExecute(object obj)
        {
            return ConnectionState == ConnectionState.Disconnected;
        }

        private void ConnectExecute(object obj)
        {
            Connect();
        }

        private void Connect()
        {
            CreatePermissionsFile();
            Disconnect(DisconnectReason.Reset);

            if (!string.IsNullOrWhiteSpace(TrackerSession.Current.Global.TwitchChannelName))
            {
                mbActive = true;

#if false
                //  WebSocket support, which is the default, is unavailable on Windows 7. Pessimistically prefer TCP connections
                //  on any OS prior to Windows 10
                OperatingSystem os = System.Environment.OSVersion;
                if (os.Version.Major < 10)
                    mClient = new TwitchClient(protocol:TwitchLib.Client.Enums.ClientProtocol.TCP);
                else
                    mClient = new TwitchClient();
#endif
                //  For now, force all connections to TCP
                mClient = new TwitchClient(protocol: TwitchLib.Client.Enums.ClientProtocol.TCP);

                mClient.OnConnected += OnConnected; ;
                mClient.OnConnectionError += OnConnectionError;
                mClient.OnMessageReceived += OnMessageReceived;
                mClient.OnChatCommandReceived += OnChatCommandReceived;
                mClient.OnDisconnected += OnDisconnected;
                mClient.OnChannelStateChanged += OnChannelStateChanged;
                mClient.OnJoinedChannel += OnJoinedChannel;
                mClient.OnLeftChannel += OnLeftChannel;
                mClient.OnReconnected += OnReconnected;
                mClient.OnLog += OnClientLog;
                mClient.Initialize(new ConnectionCredentials("EmoTracker", "oauth:za7ac5ztaeecbvqzg5ssg7gm6acug8"));

                mClient.AddChatCommandIdentifier('!');

                ConnectionState = ConnectionState.Connecting;
                mClient.Connect();
            }
        }

        private void OnClientLog(object sender, OnLogArgs e)
        {
            Log.Debug("[TwitchLib] {0}", e.Data);
        }

        private void OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
        }

        private void OnChatCommandReceived(object sender, OnChatCommandReceivedArgs e)
        {
            if (!string.Equals(e.Command.CommandText, "tracker", StringComparison.OrdinalIgnoreCase))
                return;

            HandleCommand(e.Command.ArgumentsAsList.ToArray(), e.Command.ChatMessage);
        }

        private void OnReconnected(object sender, OnReconnectedEventArgs e)
        {
        }

        private void Disconnect(DisconnectReason reason)
        {
            ConnectionState = ConnectionState.Disconnected;
            mDisconnectReason = reason;

            TwitchClient client = mClient;
            mClient = null;

            try
            {
                if (client != null)
                    client.Disconnect();
            }
            catch
            {
            }
        }

        private void OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
        }

        private void OnLeftChannel(object sender, OnLeftChannelArgs e)
        {
            Dispatch.BeginInvoke(() =>
            {
                Disconnect(DisconnectReason.Error);
            });
        }

        private void OnChannelStateChanged(object sender, OnChannelStateChangedArgs e)
        {
        }

        private void OnConnected(object sender, OnConnectedArgs e)
        {
            Dispatch.BeginInvoke(() =>
            {
                ConnectionState = ConnectionState.Connected;
                mClient.JoinChannel(TrackerSession.Current.Global.TwitchChannelName);
            });
        }

        private void OnConnectionError(object sender, OnConnectionErrorArgs e)
        {
            Dispatch.BeginInvoke(() =>
            {
                if (e.Error != null)
                    Services.DialogService.Instance.ShowOK("Twitch Connection Error", e.Error.Message);
                Services.DialogService.Instance.ShowOK("Twitch Connection Error", e.ToString());

                Disconnect(DisconnectReason.Error);
            });
        }

        private void OnDisconnected(object sender, OnDisconnectedEventArgs e)
        {
            Dispatch.BeginInvoke(() =>
            {
                mClient = null;

                Disconnect(DisconnectReason.Unknown);
            });
        }

        private void HandleCommand(string[] args, ChatMessage src)
        {
            Dispatch.BeginInvoke(() =>
            {
                try
                {
                    if (mClient == null ||!mClient.IsConnected || !mClient.JoinedChannels.Any())
                        return;

                    if (args != null && args.Length >= 1)
                    {
                        if (src.IsBroadcaster)
                        {
                            if (args[0].StartsWith("reset", StringComparison.OrdinalIgnoreCase))
                            {
                                Dispatch.BeginInvoke(() =>
                                {
                                    ApplicationModel.Instance.RefreshCommand.Execute(null);
                                });
                                return;
                            }

                            if (args[0].StartsWith("add", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
                            {
                                WhitelistUser(args[1].Trim());
                                return;
                            }

                            if (args[0].StartsWith("remove", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
                            {
                                RemoveWhitelistUser(args[1].Trim());
                                return;
                            }

                            if (args[0].StartsWith("ban", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
                            {
                                BanUser(args[1].Trim());
                                return;
                            }

                            if (args[0].StartsWith("unban", StringComparison.OrdinalIgnoreCase) && args.Length >= 2)
                            {
                                UnBanUser(args[1].Trim());
                                return;
                            }
                        }

                        if (UserIsAllowed(src))
                        {
                            Dispatch.BeginInvoke(() =>
                            {
                                ITrackableItem[] items = TrackerSession.Current.Items.FindProvidingItemsForCode(args[0]);
                                foreach (ITrackableItem item in items)
                                {
                                    ToggleItem toggle = item as ToggleItem;
                                    if (toggle != null)
                                    {
                                        if (args.Length >= 2 && args[1].StartsWith("off", StringComparison.OrdinalIgnoreCase))
                                            toggle.Active = false;
                                        else
                                            toggle.Active = true;
                                    }

                                    ProgressiveItem progressive = item as ProgressiveItem;
                                    if (progressive != null)
                                    {
                                        if (args.Length >= 2)
                                        {
                                            if (args[1].StartsWith("down", StringComparison.OrdinalIgnoreCase))
                                                progressive.Downgrade();
                                            else
                                                progressive.AdvanceToCode(args[1]);
                                        }
                                        else
                                            progressive.AdvanceToCode(args[0]);
                                    }

                                    ToggleBadgedItem badged = item as ToggleBadgedItem;
                                    if (badged != null)
                                    {
                                        if (args.Length >= 2)
                                        {
                                            if (args[1].StartsWith("off", StringComparison.OrdinalIgnoreCase))
                                                badged.Active = false;
                                            else
                                                badged.AdvanceToCode(args[1]);
                                        }
                                        else
                                            badged.AdvanceToCode(args[0]);
                                    }

                                    ConsumableItem consumable = item as ConsumableItem;
                                    if (consumable != null)
                                    {
                                        if (!consumable.SwapActions)
                                        {
                                            if (args.Length >= 2 && args[1].StartsWith("down", StringComparison.OrdinalIgnoreCase))
                                                consumable.Decrement();
                                            else
                                                consumable.Increment();
                                        }
                                        else
                                        {
                                            if (args.Length >= 2 && args[1].StartsWith("up", StringComparison.OrdinalIgnoreCase))
                                                consumable.Increment();
                                            else
                                                consumable.Decrement();
                                        }
                                    }

                                    ProgressiveToggleItem progressiveToggle = item as ProgressiveToggleItem;
                                    if (progressiveToggle != null)
                                    {
                                        if (args.Length == 1)
                                        {
                                            progressiveToggle.Active = true;
                                        }
                                        else if (args.Length >= 2)
                                        {
                                            if (args[1].StartsWith("off", StringComparison.OrdinalIgnoreCase))
                                                progressiveToggle.Active = false;
                                            else
                                                progressiveToggle.AdvanceToPrivateCode(args[1].Trim());
                                        }
                                    }
                                }
                            });

                            if (args[0].StartsWith("flush", StringComparison.OrdinalIgnoreCase))
                            {
                                Dispatch.BeginInvoke(() =>
                                {
                                    if (!src.IsBroadcaster && (System.DateTime.Now - mLastVFXCommandTime) < new System.TimeSpan(0, 0, 0, 30))
                                    {
                                        mClient.SendMessage(TrackerSession.Current.Global.TwitchChannelName, "That command has a cooldown... Wait a few seconds. Kappa");
                                        return;
                                    }

                                    mLastVFXCommandTime = System.DateTime.Now;

                                });
                                return;
                            }

                            if (args[0].StartsWith("rain", StringComparison.OrdinalIgnoreCase))
                            {
                                Dispatch.BeginInvoke(() =>
                                {
                                    if (!src.IsBroadcaster && (System.DateTime.Now - mLastVFXCommandTime) < new System.TimeSpan(0, 0, 0, 30))
                                    {
                                        mClient.SendMessage(TrackerSession.Current.Global.TwitchChannelName, "That command has a cooldown... Wait a few seconds. Kappa");
                                        return;
                                    }

                                    mLastVFXCommandTime = System.DateTime.Now;

                                });
                                return;
                            }
                        }
                    }
                }
                catch
                {
                }
            });
        }

        private string FindUserInCollection(string user, List<string> collection)
        {
            return collection.Find(value => { return String.Equals(value, user, StringComparison.OrdinalIgnoreCase) || String.Equals(value, user, StringComparison.OrdinalIgnoreCase); });
        }

        private bool IsUserWhitelisted(string user)
        {
            return FindUserInCollection(user, mUserWhitelist) != null;
        }

        private void WhitelistUser(string user)
        {
            UnBanUser(user);

            if (!IsUserWhitelisted(user))
            {
                mUserWhitelist.Add(user);
                SavePermissions();
            }
        }

        private void RemoveWhitelistUser(string user)
        {
            string wlName = FindUserInCollection(user, mUserWhitelist);
            if (wlName != null)
            {
                mUserWhitelist.Remove(wlName);
                SavePermissions();
            }
        }

        private bool IsUserBanned(string user)
        {
            return FindUserInCollection(user, mUserBlacklist) != null;
        }

        private void BanUser(string user)
        {
            RemoveWhitelistUser(user);

            if (!IsUserBanned(user))
            {
                mUserBlacklist.Add(user);
                SavePermissions();
            }
        }

        private void UnBanUser(string user)
        {
            string blName = FindUserInCollection(user, mUserBlacklist);
            if (blName != null)
            {
                mUserBlacklist.Remove(blName);
                SavePermissions();
            }
        }

        private bool UserIsAllowed(ChatMessage chatMessage)
        {
            if (IsUserWhitelisted(chatMessage.Username) || IsUserWhitelisted(chatMessage.DisplayName))
            {
                return true;
            }

            if (IsUserBanned(chatMessage.Username) || IsUserBanned(chatMessage.DisplayName))
            {
                return false;
            }

            return chatMessage.IsBroadcaster || mDefaultPermissions.HasFlag(DefaultPermissions.All) ||
                   (chatMessage.IsModerator && mDefaultPermissions.HasFlag(DefaultPermissions.Moderator)) ||
                   (chatMessage.IsVip && mDefaultPermissions.HasFlag(DefaultPermissions.VIP)) ||
                   (chatMessage.IsSubscriber && mDefaultPermissions.HasFlag(DefaultPermissions.Subscriber));
        }

        private bool mbSuspendSave = false;

        private void LoadPermissions()
        {
            lock (this)
            {
                try
                {
                    mbSuspendSave = true;

                    string path = Path.Combine(ExtensionManager.GetExtensionPath(this), "user_permissions.json");
                    if (File.Exists(path))
                    {
                        using (StreamReader reader = new StreamReader(File.OpenRead(path)))
                        {
                            JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));

                            mDefaultPermissions = root.GetEnumValue<DefaultPermissions>("default_permissions", mDefaultPermissions);

                            JArray whitelist = root.GetValue<JArray>("whitelist");
                            if (whitelist != null)
                            {
                                foreach (string user in whitelist)
                                {
                                    if (!string.IsNullOrWhiteSpace(user))
                                        WhitelistUser(user);
                                }
                            }

                            JArray blacklist = root.GetValue<JArray>("blacklist");
                            if (blacklist != null)
                            {
                                foreach (string user in blacklist)
                                {
                                    if (!string.IsNullOrWhiteSpace(user))
                                        BanUser(user);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    Services.DialogService.Instance.ShowOK("JSON Load Error", "Your Documents\\EmoTracker\\extensions\\twitch_chat_hud\\user_permissions.json file has invalid JSON. Please correct it and restart the tracker.");
                }
                finally
                {
                    mbSuspendSave = false;
                }
            }
        }

        private void CreatePermissionsFile()
        {
            string path = Path.Combine(ExtensionManager.GetExtensionPath(this), "user_permissions.json");
            if (!File.Exists(path))
            {
                SavePermissions();
            }
        }

        private void SavePermissions()
        {
            try
            {
                lock (this)
                {
                    if (mbSuspendSave)
                        return;

                    Directory.CreateDirectory(ExtensionManager.GetExtensionPath(this));

                    using (StreamWriter writer = new StreamWriter(File.Open(Path.Combine(ExtensionManager.GetExtensionPath(this), "user_permissions.json"), FileMode.Create)))
                    {
                        using (JsonTextWriter jsonWriter = new JsonTextWriter(writer))
                        {
                            jsonWriter.AutoCompleteOnClose = true;
                            jsonWriter.Formatting = Formatting.Indented;

                            JObject root = new JObject();

                            root.Add("default_permissions", JToken.FromObject(mDefaultPermissions.ToString()));

                            JArray whitelistVal = JArray.FromObject(mUserWhitelist);
                            if (whitelistVal != null)
                                root.Add("whitelist", whitelistVal);

                            JArray blacklistVal = JArray.FromObject(mUserBlacklist);
                            if (blacklistVal != null)
                                root.Add("blacklist", blacklistVal);

                            jsonWriter.WriteToken(root.CreateReader());
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }
}
