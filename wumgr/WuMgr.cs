﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WUApiLib;//this is required to use the Interfaces given by microsoft. 
using BrightIdeasSoftware;
using System.Collections;
using Microsoft.Win32;
using System.Security.AccessControl;

namespace wumgr
{
    public partial class WuMgr : Form
    {
        WuAgent agent;

        void LineLogger(object sender, AppLog.LogEventArgs args)
        {
            logBox.AppendText(args.line + Environment.NewLine);
            logBox.ScrollToCaret();
        }

        private string mWuGPO = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

        public WuMgr()
        {
            InitializeComponent();

            this.Text = Program.fmt("Windows Update Manager by David Xanatos");

            toolTip.SetToolTip(btnSearch, "Search");
            toolTip.SetToolTip(btnInstall, "Install");
            toolTip.SetToolTip(btnDownload, "Download");
            toolTip.SetToolTip(btnHide, "Hide");
            toolTip.SetToolTip(btnGetLink, "Get Links");
            toolTip.SetToolTip(btnUnInstall, "Uninstall");
            toolTip.SetToolTip(btnCancel, "Cancel");

            AppLog.Logger += LineLogger;
            agent = WuAgent.GetInstance();
            agent.Found += FoundUpdates;
            agent.Progress += OnProgress;
            agent.Init();

            updateView.ShowGroups = true;
            updateView.ShowItemCountOnGroups = true;
            updateView.AlwaysGroupByColumn = updateView.ColumnsInDisplayOrder[1];
            updateView.Sort();

            for(int i=0; i < agent.mServiceList.Count; i++){
                string service = agent.mServiceList[i];
                dlSource.Items.Add(service);
                if (service.Equals("Windows Update", StringComparison.CurrentCultureIgnoreCase))
                    dlSource.SelectedIndex = i;
            }

            mSuspendGPO = true;

            {
                var subKey = Registry.LocalMachine.CreateSubKey(mWuGPO, false);
                object value_drv = subKey.GetValue("ExcludeWUDriversInQualityUpdate");

                if (value_drv == null)
                    chkDrivers.CheckState = CheckState.Indeterminate;
                else if ((int)value_drv == 1)
                    chkDrivers.CheckState = CheckState.Unchecked;
                else if ((int)value_drv == 0)
                    chkDrivers.CheckState = CheckState.Checked;
            }

            {
                var subKey = Registry.LocalMachine.CreateSubKey(mWuGPO + @"\AU", true);
                object value_no = subKey.GetValue("NoAutoUpdate");
                if (value_no == null || (int)value_no == 0)
                {
                    object value_au = subKey.GetValue("AUOptions");
                    switch (value_au == null ? 0 : (int)value_au)
                    {
                        case 2:
                            dlPolMode.SelectedIndex = 2;
                            break;
                        case 3:
                            dlPolMode.SelectedIndex = 3;
                            break;
                        case 4:
                            dlPolMode.SelectedIndex = 4;
                            dlShDay.Enabled = dlShTime.Enabled = true;
                            break;
                        case 5:
                            dlPolMode.SelectedIndex = 5;
                            break;
                    }
                }
                else
                {
                    dlPolMode.SelectedIndex = 1;
                }

                object value_day = subKey.GetValue("ScheduledInstallDay");
                if (value_day != null)
                    dlShDay.SelectedIndex = (int)value_day;
                object value_time = subKey.GetValue("ScheduledInstallTime");
                if (value_time != null)
                    dlShTime.SelectedIndex = (int)value_time;
            }
            mSuspendGPO = false;

            UpdateCounts();
            SwitchList(UpdateLists.UpdateHistory);

            bool doUpdate = false;
            for (int i = 0; i < Program.args.Length; i++)
            {
                if (Program.args[i].Equals("-update", StringComparison.CurrentCultureIgnoreCase))
                {
                    doUpdate = true;
                }
            }

            if (doUpdate)
            {
                aTimer = new Timer();
                aTimer.Interval = 1000;
                // Hook up the Elapsed event for the timer. 
                aTimer.Tick += OnTimedEvent;
                aTimer.Enabled = true;
            }
        }

        private static Timer aTimer = null;

        private void OnTimedEvent(Object source, EventArgs e)
        {
            aTimer.Stop();
            aTimer = null;
            if (chkOffline.Checked)
                agent.SearchForUpdates((int)chkDownload.CheckState, chkOld.Checked);
            else
                agent.SearchForUpdates(dlSource.Text, chkOld.Checked);
        }

        void FoundUpdates(object sender, WuAgent.FoundUpdatesArgs args)
        {
            UpdateCounts();
            SwitchList(UpdateLists.PendingUpdates);
        }

        void UpdateCounts()
        {
            if (agent.mPendingUpdates != null)
            {
                btnWinUpd.Text = Program.fmt("Windows Update ({0})", agent.mPendingUpdates.Count);
            }
            if (agent.mInstalledUpdates != null)
            {
                btnInstalled.Text = Program.fmt("Installed Updates ({0})", agent.mInstalledUpdates.Count);
            }
            if (agent.mHiddenUpdates != null)
            {
                btnHidden.Text = Program.fmt("Hidden Updates ({0})", agent.mHiddenUpdates.Count);
            }
            if (agent.mUpdateHistory != null)
            {
                btnHistory.Text = Program.fmt("Update History ({0})", agent.mUpdateHistory.Count);
            }
        }

        void LoadList()
        {
            updateView.ClearObjects();

            updateView.CheckBoxes = CurrentList != UpdateLists.UpdateHistory;

            switch (CurrentList)
            {
                case UpdateLists.PendingUpdates:    LoadList(agent.mPendingUpdates); break;
                case UpdateLists.InstaledUpdates:   LoadList(agent.mInstalledUpdates); break;
                case UpdateLists.HiddenUpdates:     LoadList(agent.mHiddenUpdates); break;
                case UpdateLists.UpdateHistory:     LoadList(agent.mUpdateHistory); break;
            }
        }

        void LoadList(IUpdateHistoryEntryCollection List)
        {
            if (List != null)
            {
                foreach (IUpdateHistoryEntry update in List)
                {
                    updateView.AddObject(new Update(update));
                }
            }
        }

        void LoadList(UpdateCollection List)
        {
            if (List != null)
            {
                foreach (IUpdate update in List)
                {
                    updateView.AddObject(new Update(update));
                }
            }
        }

        public UpdateCollection GetUpdates()
        {
            UpdateCollection updates = new UpdateCollection();
            foreach (Update item in updateView.CheckedObjects)
            {
                updates.Add(item.Entry);
            }
            return updates;
        }

        enum UpdateLists {
            PendingUpdates,
            InstaledUpdates,
            HiddenUpdates,
            UpdateHistory
        };

        private UpdateLists CurrentList = UpdateLists.UpdateHistory;

        private bool suspendChange = false;

        void SwitchList(UpdateLists List)
        {
            if (suspendChange)
                return;

            suspendChange = true;
            btnWinUpd.CheckState = List == UpdateLists.PendingUpdates ? CheckState.Checked : CheckState.Unchecked;
            btnInstalled.CheckState = List == UpdateLists.InstaledUpdates ? CheckState.Checked : CheckState.Unchecked;
            btnHidden.CheckState = List == UpdateLists.HiddenUpdates ? CheckState.Checked : CheckState.Unchecked;
            btnHistory.CheckState = List == UpdateLists.UpdateHistory ? CheckState.Checked : CheckState.Unchecked;
            suspendChange = false;

            CurrentList = List;
            LoadList();
        }

        private void btnWinUpd_CheckedChanged(object sender, EventArgs e)
        {
            SwitchList(UpdateLists.PendingUpdates);
        }

        private void btnInstalled_CheckedChanged(object sender, EventArgs e)
        {
            SwitchList(UpdateLists.InstaledUpdates);
        }

        private void btnHidden_CheckedChanged(object sender, EventArgs e)
        {
            SwitchList(UpdateLists.HiddenUpdates);
        }

        private void btnHistory_CheckedChanged(object sender, EventArgs e)
        {
            SwitchList(UpdateLists.UpdateHistory);
        }
        

        private void btnSearch_Click(object sender, EventArgs e)
        {
            if (chkOffline.Checked)
                agent.SearchForUpdates((int)chkDownload.CheckState, chkOld.Checked);
            else
                agent.SearchForUpdates(dlSource.Text, chkOld.Checked);
        }

        private void btnDownload_Click(object sender, EventArgs e)
        {
            if (CurrentList == UpdateLists.PendingUpdates)
                agent.DownloadUpdates(GetUpdates());
        }

        private void btnInstall_Click(object sender, EventArgs e)
        {
            if (CurrentList == UpdateLists.PendingUpdates)
                agent.DownloadUpdates(GetUpdates(), true);
        }

        private void btnUnInstall_Click(object sender, EventArgs e)
        {
            if(CurrentList == UpdateLists.InstaledUpdates)
                agent.UnInstallUpdates(GetUpdates());
        }

        private void btnHide_Click(object sender, EventArgs e)
        {
            switch (CurrentList)
            {
                case UpdateLists.PendingUpdates: agent.HideUpdates(GetUpdates(), true); break;
                case UpdateLists.HiddenUpdates: agent.HideUpdates(GetUpdates(), false); break;
            }
            UpdateCounts();
            LoadList();
        }

        private void btnGetLink_Click(object sender, EventArgs e)
        {
            foreach (IUpdate update in GetUpdates())
            {
                AppLog.Line(update.Title);
                foreach (IUpdate bundle in update.BundledUpdates)
                {
                    foreach (IUpdateDownloadContent udc in bundle.DownloadContents)
                    {
                        if (String.IsNullOrEmpty(udc.DownloadUrl))
                            continue;

                        AppLog.Line(udc.DownloadUrl);
                    }
                }
                AppLog.Line("");
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            agent.CancelOperations();
        }

        void OnProgress(object sender, WuAgent.ProgressArgs args)
        {
            if (args.TotalUpdates == -1)
            {
                progTotal.Style = ProgressBarStyle.Marquee;
                progTotal.MarqueeAnimationSpeed = 30;
            }
            else
            {
                progTotal.Style = ProgressBarStyle.Continuous;
                progTotal.MarqueeAnimationSpeed = 0;

                progTotal.Value = args.TotalPercent;
            }
        }

        private void chkOffline_CheckedChanged(object sender, EventArgs e)
        {
            dlSource.Enabled = !chkOffline.Checked;
            chkDownload.Enabled = chkOffline.Checked;
        }

        private bool mSuspendGPO = false;

        private void chkDrivers_CheckStateChanged(object sender, EventArgs e)
        {
            if (mSuspendGPO)
                return;
            try
            {
                var subKey = Registry.LocalMachine.CreateSubKey(mWuGPO, true);
                switch (chkDrivers.CheckState)
                {
                    case CheckState.Unchecked:
                        subKey.SetValue("ExcludeWUDriversInQualityUpdate", 1);
                        break;
                    case CheckState.Indeterminate:
                        if (subKey.GetValue("ExcludeWUDriversInQualityUpdate") != null)
                            subKey.DeleteValue("ExcludeWUDriversInQualityUpdate");
                        break;
                    case CheckState.Checked:
                        subKey.SetValue("ExcludeWUDriversInQualityUpdate", 0);
                        break;
                }
            }
            catch (Exception err) { AppLog.Line(Program.fmt("Error: {0}",err.ToString())); }
        }

        private void dlPolMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (mSuspendGPO)
                return;
            try
            {
                dlShDay.Enabled = dlShTime.Enabled = dlPolMode.SelectedIndex == 4;

                var subKey = Registry.LocalMachine.CreateSubKey(mWuGPO + @"\AU", true);
                switch (dlPolMode.SelectedIndex)
                {
                case 0: //Automatic(default)
                    if (subKey.GetValue("NoAutoUpdate") != null)
                        subKey.DeleteValue("NoAutoUpdate");
                    if (subKey.GetValue("AUOptions") != null)
                        subKey.DeleteValue("AUOptions");
                    break;
                case 1: //Disabled
                    subKey.SetValue("NoAutoUpdate", 1);
                    if(subKey.GetValue("AUOptions") != null)
                        subKey.DeleteValue("AUOptions");
                    break;
                case 2: //Notification only
                    subKey.SetValue("NoAutoUpdate", 0);
                    subKey.SetValue("AUOptions", 2);
                    break;
                case 3: //Download only
                    subKey.SetValue("NoAutoUpdate", 0);
                    subKey.SetValue("AUOptions", 3);
                    break;
                case 4: //Schleduled Instalation
                    subKey.SetValue("NoAutoUpdate", 0);
                    subKey.SetValue("AUOptions", 4);

                    subKey.SetValue("ScheduledInstallDay", dlShDay.SelectedIndex);
                    subKey.SetValue("ScheduledInstallTime", dlShTime.SelectedIndex);
                    break;
                case 5: //Managed by Admin
                    subKey.SetValue("NoAutoUpdate", 0);
                    subKey.SetValue("AUOptions", 5);
                    break;
                }

                if (dlPolMode.SelectedIndex != 4)
                {
                    if (subKey.GetValue("ScheduledInstallDay") != null)
                        subKey.DeleteValue("ScheduledInstallDay");
                    if (subKey.GetValue("ScheduledInstallTime") != null)
                        subKey.DeleteValue("ScheduledInstallTime");
                }
            }
            catch (Exception err) { AppLog.Line(Program.fmt("Error: {0}", err.ToString())); }
        }

        private void dlShDay_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (mSuspendGPO)
                return;
            try
            {
                var subKey = Registry.LocalMachine.CreateSubKey(mWuGPO + @"\AU", true);
                subKey.SetValue("ScheduledInstallDay", dlShDay.SelectedIndex);
            }
            catch (Exception err) { AppLog.Line(Program.fmt("Error: {0}", err.ToString())); }
        }

        private void dlShTime_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (mSuspendGPO)
                return;
            try
            {
                var subKey = Registry.LocalMachine.CreateSubKey(mWuGPO + @"\AU", true);
                subKey.SetValue("ScheduledInstallTime", dlShTime.SelectedIndex);
            }
            catch (Exception err) { AppLog.Line(Program.fmt("Error: {0}", err.ToString())); }
        }
    }


    class Update : INotifyPropertyChanged
    {
        public bool IsActive = true;

        public Update(IUpdate update)
        {
            Entry = update;

            Title = update.Title;
            Category = GetCategory(update);
            Description = update.Description;
            Size = GetSizeStr(update);
            Date = update.LastDeploymentChangeTime.ToString("dd.MM.yyyy");
            KB = GetKB(update);

            try
            {
                if (update.IsBeta)
                    State = "Beta ";

                if (update.IsInstalled)
                {
                    State += "Installed";
                    if (update.IsUninstallable)
                        State += " Removable";
                }
                else if (WuAgent.safe_IsHidden(update))
                {
                    State += "Hidden";
                    if (WuAgent.safe_IsDownloaded(update))
                        State += " Downloaded";
                }
                else
                {
                    if (WuAgent.safe_IsDownloaded(update))
                        State += "Downloaded";
                    else
                        State += "Pending";
                    if (update.AutoSelectOnWebSites) //update.DeploymentAction
                        State += " (!)";
                    if (update.IsMandatory)
                        State += " Manatory";
                }
            }
            catch (Exception err) {}
        }

        public Update(IUpdateHistoryEntry update)
        {
            Title = update.Title;
            Description = update.Description;
            Date = update.Date.ToString();
        }

        string GetCategory(IUpdate update)
        {
            try
            {
                /*string category = "";
                foreach (ICategory cat in cats)
                {
                    if (category.Length > 0)
                        category += "; ";
                    category += cat.Name;
                }
                return category;*/
                return update.Categories.Count > 0 ? update.Categories[0].Name : "";
            }
            catch (Exception err) {
                return "";
            }
        }

        string GetKB(IUpdate update)
        {
            try
            {
                return update.KBArticleIDs.Count > 0 ? "KB" + update.KBArticleIDs[0] : "";
            }
            catch (Exception err)
            {
                return "";
            }
        }

        string GetSizeStr(IUpdate update)
        {
            decimal size = update.MaxDownloadSize;

            if (size > 1024 * 1024 * 1024)
                return (size / (1024 * 1024 * 1024)).ToString("F") + " Gb";
            if (size > 1024 * 1024)
                return (size / (1024 * 1024)).ToString("F") + " Mb";
            if (size > 1024 * 1024)
                return (size / (1024)).ToString("F") + " Kb";
            return ((Int64)size).ToString() + " b";
        }

        public String Title = "";
        public String Category = "";
        public String KB = "";
        public String Date = "";
        public String Size = "";
        public String Description = "";
        public String State = "";

        public IUpdate Entry = null;

        #region Implementation of INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            if (this.PropertyChanged != null)
                this.PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}
