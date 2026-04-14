using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EmoTracker.Core;
using EmoTracker.Services;
using EmoTracker.Services.Updates;
using Serilog;
using Serilog.Events;
using System;
using System.IO;

#if WINDOWS
                        if (Data.Session.TrackerSession.Current.Global.EnableDiscordRichPresence)
                        {
                            try { DiscordRpc.ClearPresence(); DiscordRpc.Shutdown(); }
                            catch { }
                        }
#endif
                    }
                    catch { }
                    finally
                    {
                        Log.CloseAndFlush();
                    }
                };
            }

            base.OnFrameworkInitializationCompleted();
        }

    }
}
