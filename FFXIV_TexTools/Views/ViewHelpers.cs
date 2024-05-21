﻿using FFXIV_TexTools.Helpers;
using FFXIV_TexTools.Properties;
using FFXIV_TexTools.Resources;
using FolderSelect;
using MahApps.Metro.Controls.Dialogs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Mods.FileTypes;
using UserControl = System.Windows.Controls.UserControl;

namespace FFXIV_TexTools.Views
{
    public static class ViewHelpers
    {
        public static Progress<(int current, int total, string message)> BindReportProgress(ProgressDialogController controller)
        {
            return new Progress<(int current, int total, string message)>(BindReportProgressAction(controller));
        }
        public static Action<(int current, int total, string message)> BindReportProgressAction(ProgressDialogController controller)
        {
            Action<(int current, int total, string message)> f = ((int current, int total, string message) report) =>
            {
                var message = "";
                if (!report.message.Equals(string.Empty))
                {
                    message =report.message.L();
                }
                else
                {
                    message = $"{UIMessages.PleaseStandByMessage}";
                }

                if (report.total > 0)
                {
                    var value = (double)report.current / (double)report.total;
                    controller.SetProgress(value);

                    message += "\n\n " + report.current + "/" + report.total;
                }
                else
                {
                    controller.SetIndeterminate();
                }

                controller.SetMessage(message);
            };

            return f;
        }

        public static void ShowError(this UserControl control, string title, string message)
        {
            var wind = Window.GetWindow(control);
            var Win32Window = new WindowWrapper(new WindowInteropHelper(wind).Handle);
            FlexibleMessageBox.Show(Win32Window, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1);
            
        }

        public static void ShowError(string title, string message)
        {
            FlexibleMessageBox.Show(MainWindow.GetMainWindow().Win32Window, message, title, MessageBoxButtons.OK, MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1);
        }

        public static Action Debounce(Action func, int milliseconds = 300)
        {
            CancellationTokenSource cancelTokenSource = null;

            return () =>
            {
                cancelTokenSource?.Cancel();
                cancelTokenSource = new CancellationTokenSource();

                Task.Delay(milliseconds, cancelTokenSource.Token)
                    .ContinueWith(t =>
                    {
                        if (t.IsCompleted && !t.IsCanceled)
                        {
                            func();
                        }
                    }, TaskScheduler.Default);
            };
        }
        public static Action<T> Debounce<T>(Action<T> func, int milliseconds = 300)
        {
            CancellationTokenSource cancelTokenSource = null;

            return arg =>
            {
                cancelTokenSource?.Cancel();
                cancelTokenSource = new CancellationTokenSource();

                Task.Delay(milliseconds, cancelTokenSource.Token)
                    .ContinueWith(t =>
                    {
                        if (t.IsCompleted && !t.IsCanceled)
                        {
                            func(arg);
                        }
                    }, TaskScheduler.Default);
            };
        }


        /// <summary>
        /// Gets an import settings object with most of the standard user-controlled or system standard stuff set already.
        /// </summary>
        /// <returns></returns>
        public static ModPackImportSettings GetDefaultImportSettings(ProgressDialogController controller = null)
        {
            var settings = new ModPackImportSettings();
            settings.AutoAssignSkinMaterials = Properties.Settings.Default.AutoMaterialFix;
            settings.UpdateDawntrailMaterials = Properties.Settings.Default.FixPreDawntrailOnImport;
            settings.RootConversionFunction = ModpackRootConvertWindow.GetRootConversions;
            settings.SourceApplication = XivStrings.TexTools;

            if (controller != null)
            {
                settings.ProgressReporter = BindReportProgress(controller);
            }
            return settings;
        }


        /// <summary>
        /// Gets the race from the settings
        /// </summary>
        /// <param name="gender">The gender of the currently selected race</param>
        /// <returns>A tuple containing the race and body</returns>
        public static (XivRace Race, string BodyID) GetUserRace(int gender)
        {
            var settingsRace = Settings.Default.Default_Race;
            var defaultBody = "0001";

            if (settingsRace.Equals(XivStringRaces.Hyur_M))
            {
                if (gender == 0)
                {
                    return (XivRaces.GetXivRace("0101"), defaultBody);
                }
            }

            if (settingsRace.Equals(XivStringRaces.Hyur_H))
            {
                if (gender == 0)
                {
                    return (XivRaces.GetXivRace("0301"), defaultBody);
                }

                return (XivRaces.GetXivRace("0401"), defaultBody);
            }

            if (settingsRace.Equals(XivStringRaces.Aura_R))
            {
                if (gender == 0)
                {
                    return (XivRaces.GetXivRace("1301"), defaultBody);
                }

                return (XivRaces.GetXivRace("1401"), defaultBody);
            }

            if (settingsRace.Equals(XivStringRaces.Aura_X))
            {
                if (gender == 0)
                {
                    return (XivRaces.GetXivRace("1301"), "0101");
                }

                return (XivRaces.GetXivRace("1401"), "0101");
            }

            return (XivRaces.GetXivRace("0201"), defaultBody);
        }

    }
}
