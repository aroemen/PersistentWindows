﻿using System;
using System.Threading;
using System.Windows.Forms;
using System.Diagnostics;

using Ninjacrab.PersistentWindows.Common;
using Ninjacrab.PersistentWindows.Common.Diagnostics;

namespace Ninjacrab.PersistentWindows.SystrayShell
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        public static readonly string ProjectUrl = "https://github.com/kangyu-california/PersistentWindows";

        static PersistentWindowProcessor pwp = null;    
        static SystrayForm systrayForm = null;
        static bool silent = false; //suppress all balloon tip & sound prompt
        static bool notification = false; //pop balloon when auto restore

        [STAThread]
        static void Main(string[] args)
        {
            bool splash = true;
            bool dry_run = false; //dry run mode without real restore, for debug purpose only
            bool fix_zorder = true;
            bool delay_start = false;
            bool redraw_desktop = false;
            bool redirect_appdata = false; // use "." instead of appdata/local/PersistentWindows to store db file
            bool offscreen_fix = true;
            bool enhanced_offscreen_fix = false;
            bool prompt_session_restore = false;

            foreach (var arg in args)
            {
                if (delay_start)
                {
                    delay_start = false;
                    int seconds = Int32.Parse(arg);
                    Thread.Sleep(1000 * seconds);
                    continue;
                }

                switch(arg)
                {
                    case "-silent":
                        silent = true;
                        splash = false;
                        break;
                    case "-splash_off":
                        splash = false;
                        break;
                    case "-splash=0":
                        splash = false;
                        break;
                    case "-notification_on":
                        notification = true;
                        break;
                    case "-notification=1":
                        notification = true;
                        break;
                    case "-dry_run":
                        dry_run = true;
                        break;
                    case "-fix_zorder=0":
                        fix_zorder = false;
                        break;
                    case "-delay_start":
                        delay_start = true;
                        break;
                    case "-redraw_desktop":
                        redraw_desktop = true;
                        break;
                    case "-redirect_appdata":
                        redirect_appdata = true;
                        break;
                    case "-enhanced_offscreen_fix":
                        enhanced_offscreen_fix = true;
                        break;
                    case "-disable_offscreen_fix":
                        offscreen_fix = false;
                        break;
                    case "-offscreen_fix=0":
                        offscreen_fix = false;
                        break;
                    case "-prompt_session_restore":
                        prompt_session_restore = true;
                        break;
                }
            }

            while (String.IsNullOrEmpty(PersistentWindowProcessor.GetDisplayKey()))
            {
                Thread.Sleep(5000);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            systrayForm = new SystrayForm();

            pwp = new PersistentWindowProcessor();
            pwp.dryRun = dry_run;
            pwp.fixZorder = fix_zorder;
            pwp.showRestoreTip = ShowRestoreTip;
            pwp.hideRestoreTip = HideRestoreTip;
            pwp.enableRestoreMenu = EnableRestoreMenu;
            pwp.redrawDesktop = redraw_desktop;
            pwp.redirectAppDataFolder = redirect_appdata;
            pwp.enhancedOffScreenFix = enhanced_offscreen_fix;
            pwp.enableOffScreenFix = offscreen_fix;
            pwp.promptSessionRestore = prompt_session_restore;

            if (!pwp.Start())
            {
                systrayForm.notifyIconMain.Visible = false;
                return;
            }

            if (splash)
            {
                StartSplashForm();
            }

            Application.Run();
        }

        static void ShowRestoreTip()
        {
            var thread = new Thread(() =>
            {
                if (silent)
                    return;

                systrayForm.notifyIconMain.Visible = false;
                systrayForm.notifyIconMain.Visible = true;

                if (!notification)
                    return;

                systrayForm.notifyIconMain.ShowBalloonTip(10000);
            });

            thread.IsBackground = false;
            thread.Start();
        }

        static void HideRestoreTip()
        {
            if (silent || !notification)
                return;
            systrayForm.notifyIconMain.Visible = false;
            systrayForm.notifyIconMain.Visible = true;
        }

        static void EnableRestoreMenu(bool enable)
        {
#if DEBUG
            LogEvent("start ui refresh timer");
#endif
            systrayForm.enableRestoreFromDB = enable;
            systrayForm.enableRefresh = true;
        }

        static public void TakeSnapshot()
        {
            pwp.TakeSnapshot();
            if (!silent)
                systrayForm.notifyIconMain.ShowBalloonTip(5000, "snapshot captured", "click icon to restore the snapshot", ToolTipIcon.Info);
        }

        static void StartSplashForm()
        {
            var thread = new Thread(() =>
            {
                Application.Run(new SplashForm());
            });
            thread.IsBackground = false;
            thread.Priority = ThreadPriority.Highest;
            thread.Name = "StartSplashForm";
            thread.Start();
        }

        static public void ManageLayoutProfile()
        {
            var profileDlg = new LayoutProfile();
            if (profileDlg.ShowDialog(systrayForm) == DialogResult.OK)
            {

            }
        }

        static public void Capture()
        {
            GetProcessInfo();
            pwp.BatchCaptureApplicationsOnCurrentDisplays(saveToDB : true);
        }

        static public void RestoreDisk()
        {
            pwp.restoringFromDB = true;
            pwp.StartRestoreTimer(milliSecond : 2000 /*wait mouse settle still for taskbar restore*/);
        }

        static public void RestoreSnapshot()
        {
            if (!pwp.SnapshotExist())
                return;

            pwp.restoringFromMem = true;
            pwp.restoringSnapshot = true;
            pwp.StartRestoreTimer(milliSecond : 500 /*wait mouse settle still for taskbar restore*/);
            LogEvent("restore snapshot");
        }

        static public void PauseAutoRestore()
        {
            pwp.pauseAutoRestore = true;
            pwp.sessionActive = false; //disable capture as well
        }

        static public void ResumeAutoRestore()
        {
            pwp.pauseAutoRestore = false;
            pwp.restoringFromMem = true;
            pwp.StartRestoreTimer();
        }
        static void GetProcessInfo()
        {
            Process process = new Process();
            process.StartInfo.FileName = "wmic.exe";
            //process.StartInfo.Arguments = "process get caption,commandline,processid /format:csv";
            process.StartInfo.Arguments = "process get commandline,processid /format:csv";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = false;

            process.OutputDataReceived += new DataReceivedEventHandler(OutputHandler);
            process.ErrorDataReceived += new DataReceivedEventHandler(OutputHandler);

            // Start process and handlers
            process.Start();
            process.BeginOutputReadLine();
            //process.BeginErrorReadLine();
            process.WaitForExit();
        }

        static void OutputHandler(object sendingProcess, DataReceivedEventArgs outLine)
        {
            //* Do your stuff with the output (write to console/log/StringBuilder)
            //Console.WriteLine(outLine.Data);
            string line = outLine.Data;
            if (string.IsNullOrEmpty(line))
                return;
            //Log.Info("{0}", line);
            string[] fields = line.Split(',');
            if (fields.Length < 3)
                return;
            uint processId;
            if (uint.TryParse(fields[2], out processId))
            {
                if (!string.IsNullOrEmpty(fields[1]))
                {
                    pwp.processCmd[processId] = fields[1];
                }
            }
        }
        public static void LogEvent(string format, params object[] args)
        {
            Log.Event(format, args);
        }
    }
}
