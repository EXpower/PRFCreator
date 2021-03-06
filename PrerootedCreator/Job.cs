﻿using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;

namespace PRFCreator
{
    class Job
    {
        public static Form1 form;

        private static int JobNum = 0;
        private static void SetJobNum(int num)
        {
            if (form.jobnum_label.InvokeRequired)
                form.jobnum_label.Invoke(new MethodInvoker(delegate { form.jobnum_label.Text = num + "/" + GetJobCount(); }));
            else
                form.jobnum_label.Text = num + "/" + GetJobCount();
        }

        private static int GetJobCount()
        {
            int count = jobs.Length - 1; //don't count 'Complete'
            if (form.include_checklist.CheckedItems.Count < 1) //if there are no extra files
                count--;
            if (form.options_checklist.CheckedItems.Count < 1)
                count--;
            if (!File.Exists(form.rec_textbox.Text)) //if recovery is not included
                count--;
            if (form.extra_listbox.Items.Count < 1) //no additional zip files
                count--;

            return count;
        }

        private static Action<BackgroundWorker>[] jobs = { UnpackSystem, UnpackSystemEXT4, EditScript, AddSystem, AddExtras, AddSuperSU, AddRecovery, AddExtraFlashable, SignZip, Complete };
        public static void Worker()
        {
            JobNum = 0;
            int free = Utility.freeSpaceMB(Path.GetTempPath());
            if (free < 4096)
            {
                Logger.WriteLog("Error: Not enough disk space. Please make sure that you have atleast 4GB free space on drive " + Path.GetPathRoot(Path.GetTempPath())
                    + ". Currently you only have " + free + "MB available");
                return;
            }
            if (!Zipping.ExistsInZip(form.ftf_textbox.Text, "system.sin"))
            {
                Logger.WriteLog("Error: system.sin does not exist in file " + form.ftf_textbox.Text);
                return;
            }
            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.WorkerSupportsCancellation = true;
            worker.DoWork += (o, _e) =>
                {
                    try
                    {
                        form.ControlsEnabled(false);
                        foreach (Action<BackgroundWorker> action in jobs)
                        {
                            if (worker.CancellationPending)
                            {
                                Cancel(worker);
                                _e.Cancel = true;
                                break;
                            }
                            action(worker);
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.WriteLog(e.Message);
                        //Logger.WriteLog(e.StackTrace);
                    }
                };
            worker.ProgressChanged += (o, _e) =>
                {
                    form.progressBar.Value = _e.ProgressPercentage;
                };
            worker.RunWorkerCompleted += (o, _e) =>
                {
                    form.ControlsEnabled(true);
                };
            worker.RunWorkerAsync();
        }

        private static void UnpackSystem(BackgroundWorker worker)
        {
            SetJobNum(++JobNum);
            Logger.WriteLog("Extracting system.sin from " + System.IO.Path.GetFileName(form.ftf_textbox.Text));
            if (!Zipping.UnzipFile(worker, form.ftf_textbox.Text, "system.sin", string.Empty, System.IO.Path.GetTempPath()))
            {
                worker.CancelAsync();
                return;
            }

            byte[] UUID = PartitionInfo.ReadSinUUID(Path.Combine(Path.GetTempPath(), "system.sin"));
            PartitionInfo.UsingUUID = (UUID != null);
            Utility.ScriptSetUUID(worker, "system", UUID);
        }

        private static void EditScript(BackgroundWorker worker)
        {
            SetJobNum(++JobNum);
            Logger.WriteLog("Adding info to flashable script");
            string fw = Utility.PadStr(Path.GetFileNameWithoutExtension(form.ftf_textbox.Text), " ", 41);
            Utility.EditScript(worker, "INSERT FIRMWARE HERE", fw);
        }

        private static void UnpackSystemEXT4(BackgroundWorker worker)
        {
            SetJobNum(++JobNum);
            SinExtract.ExtractSin(worker, Path.Combine(Path.GetTempPath(), "system.sin"), Path.Combine(Path.GetTempPath(), "system.ext4"));
            File.Delete(Path.Combine(Path.GetTempPath(), "system.sin"));
        }

        private static void AddSystem(BackgroundWorker worker)
        {
            SetJobNum(++JobNum);
            Logger.WriteLog("Adding system to zip");
            Zipping.AddToZip(worker, "flashable.zip", Path.Combine(Path.GetTempPath(), "system.ext4"), "system.ext4");
            File.Delete(Path.Combine(Path.GetTempPath(), "system.ext4"));
        }

        private static void AddExtras(BackgroundWorker worker)
        {
            if (form.include_checklist.CheckedItems.Count < 1)
                return;

            Logger.WriteLog("Adding extra files");
            SetJobNum(++JobNum);
            foreach (string item in form.include_checklist.CheckedItems)
                ExtraFiles.AddExtraFiles(worker, item.ToLower(), form.ftf_textbox.Text);
        }

        private static void AddExtraFlashable(BackgroundWorker worker)
        {
            if (form.extra_listbox.Items.Count < 1)
                return;

            SetJobNum(++JobNum);
            foreach (string file in form.extra_listbox.Items)
                ExtraFiles.AddExtraFlashable(worker, file, form.ftf_textbox.Text);
        }

        private static void AddSuperSU(BackgroundWorker worker)
        {
            SetJobNum(++JobNum);
            Logger.WriteLog("Adding " + Path.GetFileName(form.su_textbox.Text));
            string superSUFile = form.su_textbox.Text;
            Zipping.AddToZip(worker, "flashable.zip", superSUFile, "SuperSU.zip", false);
        }

        private static void AddRecovery(BackgroundWorker worker)
        {
            if (!File.Exists(form.rec_textbox.Text))
                return;

            SetJobNum(++JobNum);
            string recoveryFile = form.rec_textbox.Text;
            Logger.WriteLog("Adding " + Path.GetFileName(recoveryFile));
            Zipping.AddToZip(worker, "flashable.zip", recoveryFile, "dualrecovery.zip");
        }

        //~ doubles the process time
        private static void SignZip(BackgroundWorker worker)
        {
            if (!form.options_checklist.CheckedItems.Contains("Sign zip"))
                return;

            SetJobNum(++JobNum);
            if (!Utility.JavaInstalled())
            {
                Logger.WriteLog("Error: Could not execute Java. Is it installed?");
                return;
            }
            if (!File.Exists("signapk.jar"))
            {
                Logger.WriteLog("Error: signapk.jar file not found");
                return;
            }

            Utility.WriteResourceToFile("PRFCreator.Resources.testkey.pk8", "testkey.pk8");
            Utility.WriteResourceToFile("PRFCreator.Resources.testkey.x509.pem", "testkey.x509.pem");

            Logger.WriteLog("Signing zip file");
            if (Utility.RunProcess("java", "-Xmx1024m -jar signapk.jar -w testkey.x509.pem testkey.pk8 flashable.zip flashable-prerooted-signed.zip") == 0)
                File.Delete("flashable.zip");
            else
                Logger.WriteLog("Error: Could not sign zip");

            File.Delete("testkey.pk8");
            File.Delete("testkey.x509.pem");
        }

        private static void Complete(BackgroundWorker worker)
        {
            if (File.Exists("flashable.zip"))
                File.Move("flashable.zip", "flashable-prerooted.zip");

            Logger.WriteLog("Finished\n");
        }

        private static void Cancel(BackgroundWorker worker)
        {
            Logger.WriteLog("Cancelled\n");
        }
    }
}
