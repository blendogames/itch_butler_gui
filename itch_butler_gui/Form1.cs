using System;
using System.Collections.Generic;
using System.ComponentModel;
//using System.Data;
using System.Drawing;
//using System.Linq;
//using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

//TODO:

//File > Save

//uploadtype: soundtrack, executable, etc

namespace itch_butler_gui
{
    public partial class Form1 : Form
    {
        const string PROFILE_FILE = "profiles.json";
        const string PROFILE_BACKUP = "profiles.backup";
        const string BUTLER_EXE = "butler.exe";
        const string BUTLER_URL = "https://fasterthanlime.itch.io/butler";
        const string DEFAULT_ARGS = "";

        List<ProjectProfile> profiles;
        int lastSelectedIndex = -1;

        BackgroundWorker backgroundWorker; //We do things on background thread so user can still drag window around while build is uploading.
        DateTime start;

        int buildIndex; //When iterating through all the builds for the upload process.
        int profileIndex; //Which profile to upload.

        public Form1()
        {
            InitializeComponent();

            this.FormClosed += MyClosedHandler;

            bool foundError = false;

            if (!File.Exists(BUTLER_EXE))
            {
                AddLog(string.Format("Failed to find {0}. Please install Butler:", BUTLER_EXE, Environment.CurrentDirectory));
                AddLog(string.Format("  1. Download Butler from {0}", BUTLER_URL));
                AddLog(string.Format("  2. Put all Butler files into {0}", Environment.CurrentDirectory));
                AddLog(string.Empty);
                listBox1.BackColor = Color.Pink;
                SetButtonsEnabled(false);
                foundError = true;
                Shown += Form1_Shown;
            }

            //Attempt to load profiles.
            if (!LoadProfiles(out profiles))
            {
                listBox1.BackColor = Color.Pink;
                AddLog("No profiles found. Please go to: File > Add new profile");
                SetButtonsEnabled(true);

                button1.Enabled = false;
                comboBox1.Enabled = false;
                dataGridView1.Enabled = false;
                textBox_username.Enabled = false;
                textBox_gamename.Enabled = false;
                addNewProjectProfileToolStripMenuItem.Enabled = true;
                profileManagementToolStripMenuItem.Enabled = false;

                foundError = true;
            }            

            if (foundError)
            {
                //Popup box for butler download.
                return;
            }



            //AddLog(string.Format("Found {0} profile{1}.", profiles.Count, profiles.Count > 1 ? "s" : string.Empty));
            //AddLog(string.Empty);
            AddLog("-- READY TO UPLOAD --");

            //Populate the profiles dropdown box.
            for (int i = 0; i < profiles.Count; i++)
            {
                comboBox1.Items.Add(profiles[i].profilename);
            }

            comboBox1.SelectedIndexChanged += new EventHandler(ComboBox1_SelectedIndexChanged);

            //Attempt to load the last-selected profile from previous session.
            if (!string.IsNullOrWhiteSpace(Properties.Settings.Default.lastSelectedProfile))
            {
                int targetIndex = comboBox1.FindStringExact(Properties.Settings.Default.lastSelectedProfile);

                if (targetIndex >= 0)
                    comboBox1.SelectedIndex = targetIndex;
                else
                    comboBox1.SelectedIndex = 0;
            }
            else
            {
                //Default to first profile.
                comboBox1.SelectedIndex = 0;
            }

            dataGridView1.CellValueChanged += new DataGridViewCellEventHandler(dataGridView1_CellValueChanged);
            dataGridView1.LostFocus += new EventHandler(datagrid_LostFocus);

            //textBox_username.TextChanged += new EventHandler(textboxValuechanged);
            //textBox_gamename.TextChanged += new EventHandler(textboxValuechanged);

            textBox_gamename.LostFocus += new EventHandler(txtbox_LostFocus);
            textBox_username.LostFocus += new EventHandler(txtbox_LostFocus);

            
        }


        private void Form1_Shown(Object sender, EventArgs e)
        {
            if (MessageBox.Show("Itch.io Butler is required to be installed in the same folder as this program. Would you like to open the Butler download page?\n\nhttps://fasterthanlime.itch.io/butler", "Blendo itch uploader", MessageBoxButtons.YesNo) == System.Windows.Forms.DialogResult.Yes)
            {
                //Open up the Butler download page.
                try
                {
                    Process.Start("https://fasterthanlime.itch.io/butler");
                    AddLog("Blendo itch uploader");
                }
                catch (Exception err)
                {
                    AddLog(string.Format("Failed to open Butler download page. {0}", err.Message));
                }
            }
        }



        private void datagrid_LostFocus(object sender, EventArgs e)
        {
            dataGridView1.ClearSelection();
        }

        private void txtbox_LostFocus(object sender, EventArgs e)
        {
            if (comboBox1.SelectedIndex < 0)
                return;

            profiles[comboBox1.SelectedIndex].username = textBox_username.Text;
            profiles[comboBox1.SelectedIndex].gamename = textBox_gamename.Text;
        }

        //private void textboxValuechanged(object sender, EventArgs e)
        //{
        //    //update profile.
        //    if (comboBox1.SelectedIndex < 0)
        //        return;
        //
        //    profiles[comboBox1.SelectedIndex].username = textBox_username.Text;
        //    profiles[comboBox1.SelectedIndex].gamename = textBox_gamename.Text;
        //}

        void dataGridView1_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            //var editedCell = this.dataGridView1.Rows[e.RowIndex].Cells[e.ColumnIndex];
            //var newValue = editedCell.Value;

            //Commit changes to the profiles array.
            List<ProfileBuilds> buildList = new List<ProfileBuilds>();

            for (int i = 0; i < dataGridView1.Rows.Count; i++)
            {
                if (dataGridView1.Rows[i].Cells[1].Value == null || dataGridView1.Rows[i].Cells[2].Value == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(dataGridView1.Rows[i].Cells[1].Value.ToString()) || string.IsNullOrWhiteSpace(dataGridView1.Rows[i].Cells[2].Value.ToString()))
                {
                    continue;
                }

                ProfileBuilds newBuild = new ProfileBuilds();
                newBuild.platform = dataGridView1.Rows[i].Cells[1].Value.ToString();
                newBuild.folder = dataGridView1.Rows[i].Cells[2].Value.ToString();
                newBuild.ignorefilters = dataGridView1.Rows[i].Cells[3].Value.ToString();
                buildList.Add(newBuild);
            }

            profiles[comboBox1.SelectedIndex].builds = buildList.ToArray();
        }

        protected void MyClosedHandler(object sender, EventArgs e)
        {
            SaveSettings();
        }

        private void SaveSettings()
        {
            //Before saving, make a backup file.
            try
            {
                File.Copy(PROFILE_FILE, PROFILE_BACKUP, true);
            }
            catch (Exception err)
            {
                AddLog(string.Format("Error in making backup file. Error: {0}", err.Message));
            }

            //Save changes to json file.
            try
            {
                using (StreamWriter file = File.CreateText(PROFILE_FILE))
                {
                    //JsonSerializer serializer = new JsonSerializer();
                    //serializer.Serialize(file, profiles);

                    //Nice formatting:
                    string output = JsonConvert.SerializeObject(profiles, Formatting.Indented);
                    file.Write(output);
                }

                if (comboBox1.SelectedIndex >= 0)
                {
                    Properties.Settings.Default.lastSelectedProfile = comboBox1.Items[comboBox1.SelectedIndex].ToString();
                    Properties.Settings.Default.Save();
                }
            }
            catch (Exception err)
            {
                MessageBox.Show(string.Format("Error in saving {0} file.\n\nError:\n{1}", PROFILE_FILE, err.Message), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        //This gets called when a profile is selected.
        private void ComboBox1_SelectedIndexChanged(object sender, System.EventArgs e)
        {
            if (lastSelectedIndex == comboBox1.SelectedIndex)
                return; //If select same profile, then ignore.

            lastSelectedIndex = comboBox1.SelectedIndex;

            //Load the username stuff.
            if (!string.IsNullOrWhiteSpace(profiles[comboBox1.SelectedIndex].username))
                textBox_username.Text = profiles[comboBox1.SelectedIndex].username;
            else
                textBox_username.Text = string.Empty;

            if (!string.IsNullOrWhiteSpace(profiles[comboBox1.SelectedIndex].gamename))
                textBox_gamename.Text = profiles[comboBox1.SelectedIndex].gamename;
            else
                textBox_gamename.Text = string.Empty;

            dataGridView1.Rows.Clear();
            dataGridView1.Refresh();

            if (profiles[comboBox1.SelectedIndex].builds == null)
                return;

            if (profiles[comboBox1.SelectedIndex].builds.Length <= 0)
                return;

            for (int i = 0; i < profiles[comboBox1.SelectedIndex].builds.Length; i++)
            {
                //Populate the data grid fields.
                //dataGridView1.add

                DataGridViewRow row = new DataGridViewRow();
                row.CreateCells(dataGridView1);  // this line was missing
                row.Cells[0].Value = true;
                row.Cells[1].Value = profiles[comboBox1.SelectedIndex].builds[i].platform;
                row.Cells[2].Value = profiles[comboBox1.SelectedIndex].builds[i].folder;
                row.Cells[3].Value = profiles[comboBox1.SelectedIndex].builds[i].ignorefilters;
                dataGridView1.Rows.Add(row);
            }

            //dataGridView1[0, 0].Selected = true;

            //comboBox1.SelectedIndex
        }

        private void SetButtonsEnabled(bool value)
        {
            button1.Enabled = value;
            comboBox1.Enabled = value;
            dataGridView1.Enabled = value;
            textBox_username.Enabled = value;
            textBox_gamename.Enabled = value;
            addNewProjectProfileToolStripMenuItem.Enabled = value;
            profileManagementToolStripMenuItem.Enabled = value;

            if (!value)
                dataGridView1.DefaultCellStyle.BackColor = Color.DarkGray;
            else
                dataGridView1.DefaultCellStyle.BackColor = Color.White;
        }

        private bool LoadProfiles(out List<ProjectProfile> output)
        {
            SetButtonsEnabled(false);

            output = new List<ProjectProfile>();

            if (!File.Exists(PROFILE_FILE))
            {
                //AddLog(string.Format("No {0} found.", PROFILE_FILE));
                return false;
            }

            string fileContents = GetFileContents(PROFILE_FILE);

            if (string.IsNullOrWhiteSpace(fileContents))
            {
                return false;
            }

            //Load the profiles json file.
            //AddLog(string.Format("Loading {0}", PROFILE_FILE));
            
            try
            {
                output = JsonConvert.DeserializeObject<List<ProjectProfile>>(fileContents);
            }
            catch (Exception e)
            {
                AddLog(string.Format("Error: can't parse {0}. Error: {1}", PROFILE_FILE, e.Message));
                return false;
            }

            if (output.Count <= 0)
            {
                return false;
            }

            SetButtonsEnabled(true);
            return true;
        }

        private string GetFileContents(string filepath)
        {
            string output = "";

            try
            {
                using (FileStream stream = File.Open(filepath, FileMode.Open, FileAccess.Read))
                {
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        //dump file contents into a string.
                        output = reader.ReadToEnd();
                    }
                }
            }
            catch(Exception e)
            {
                AddLog(string.Format("Error: can't read \n{0}. Error: {1}", filepath, e.Message));
                listBox1.BackColor = Color.Pink;
                return string.Empty;
            }

            return output;
        }

        private void AddLog(string text)
        {
            listBox1.Items.Add(text);

            int nItems = (int)(listBox1.Height / listBox1.ItemHeight);
            listBox1.TopIndex = listBox1.Items.Count - nItems;

            this.Update();
            this.Refresh();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void addNewProjectProfileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //Add new profile.
            listBox1.BackColor = Color.White;

            string promptValue = Prompt.ShowDialog("What is the name of the new profile?", "New profile");
            promptValue = promptValue.Trim();

            if (string.IsNullOrWhiteSpace(promptValue))
            {
                listBox1.BackColor = Color.Pink;
                AddLog("Error: can't use empty profile name.");
                return;
            }

            //Check if it already exists.
            for (int i = 0; i < comboBox1.Items.Count; i++)
            {
                string comboboxText = (comboBox1.Items[i]).ToString();

                if (string.Compare(comboboxText, promptValue, StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    listBox1.BackColor = Color.Pink;
                    AddLog(string.Format("Error: profile '{0}' already exists.", promptValue));
                    return;
                }
            }

            ProjectProfile newProfile = new ProjectProfile();
            newProfile.profilename = promptValue;
            newProfile.arguments = DEFAULT_ARGS; //Default arguments.
            newProfile.gamename = string.Empty;
            newProfile.username = string.Empty;
            profiles.Add(newProfile);

            textBox_username.Text = string.Empty;
            textBox_gamename.Text = string.Empty;

            comboBox1.Items.Add(promptValue);
            comboBox1.SelectedIndex = comboBox1.FindStringExact(promptValue);

            AddLog(string.Empty);
            AddLog(string.Format("Added new profile: {0}", promptValue));
            listBox1.BackColor = Color.White;
            SetButtonsEnabled(true);
        }

        private void openProfileFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;            

            try
            {
                Process.Start(PROFILE_FILE);
            }
            catch (Exception err)
            {
                AddLog(string.Format("Error: {0}", err.Message));
                listBox1.BackColor = Color.Pink;
            }
        }

        private void deleteCurrentProfileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;            

            if (comboBox1.SelectedIndex < 0)
            {
                AddLog("Error: can't delete, no profile selected.");
                return;
            }

            string profilename = (comboBox1.Items[comboBox1.SelectedIndex]).ToString();

            profiles.RemoveAt(comboBox1.SelectedIndex);
            comboBox1.Items.RemoveAt(comboBox1.SelectedIndex);

            if (comboBox1.Items.Count > 0)
                comboBox1.SelectedIndex = 0;

            AddLog(string.Format("Deleted profile: {0}", profilename));

            if (profiles.Count <= 0)
            {
                SetButtonsEnabled(false);

                //Clear out the data grid.
                dataGridView1.Rows.Clear();
            }
        }

        //Return TRUE if everything's fine.
        private bool DoSanityCheck()
        {
            bool success = true;

            if (string.IsNullOrWhiteSpace(textBox_username.Text))
            {
                AddLog("Error: username field is empty.");
                success = false;
            }

            if (string.IsNullOrWhiteSpace(textBox_gamename.Text))
            {
                AddLog("Error: game name field is empty.");
                success = false;
            }

            if (!File.Exists(BUTLER_EXE))
            {
                AddLog(string.Format("Error: failed to find {0}. Please copy all Butler files into {1}", BUTLER_EXE, Environment.CurrentDirectory));
                success = false;
            }

            if (!success)
            {
                listBox1.BackColor = Color.Pink;
            }

            return success;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;

            dataGridView1.ClearSelection();

            if (!DoSanityCheck())
            {
                return;
            }            

            int platformCount = 0;
            for (int i = 0; i < dataGridView1.Rows.Count; i++)
            {
                if (dataGridView1.Rows[i].Cells[0].Value == null)
                    continue;

                if (!(bool)(dataGridView1.Rows[i].Cells[0].Value)) //Only check platforms that user has checkbox'd.
                    continue;

                //If BOTH fields are empty, then ignore it.
                if (dataGridView1.Rows[i].Cells[1].Value == null && dataGridView1.Rows[i].Cells[2].Value == null)
                    continue;

                //If ONE field is empty, then it's an error.
                if ((dataGridView1.Rows[i].Cells[1].Value == null && dataGridView1.Rows[i].Cells[2].Value != null)
                    || (dataGridView1.Rows[i].Cells[1].Value != null && dataGridView1.Rows[i].Cells[2].Value == null))
                {
                    AddLog("Error: one of the Platform / Local folder fields is empty.");
                    listBox1.BackColor = Color.Pink;
                    return;
                }

                if ((string.IsNullOrWhiteSpace(dataGridView1.Rows[i].Cells[1].Value.ToString()) && !string.IsNullOrWhiteSpace(dataGridView1.Rows[i].Cells[2].Value.ToString()))
                    || (!string.IsNullOrWhiteSpace(dataGridView1.Rows[i].Cells[1].Value.ToString()) && string.IsNullOrWhiteSpace(dataGridView1.Rows[i].Cells[2].Value.ToString())))
                {
                    AddLog("Error: one of the platform / folder fields is empty.");
                    listBox1.BackColor = Color.Pink;
                    return;
                }

                //Do directory sanity check.
                string foldername = dataGridView1.Rows[i].Cells[2].Value.ToString();
                if (!Directory.Exists(foldername))
                {
                    AddLog(string.Format("Error: can't find folder: {0}", foldername));
                    listBox1.BackColor = Color.Pink;
                    return;
                }

                platformCount++;
            }

            if (platformCount <= 0)
            {
                AddLog("Error: nothing to upload.");
                listBox1.BackColor = Color.Pink;
                return;
            }

            SetButtonsEnabled(false);
            start = DateTime.Now;

            AddLog(string.Empty);
            AddLog(string.Format("-- STARTING UPLOAD -- ({0} build{1})", platformCount, platformCount > 1 ? "s" : string.Empty));

            buildIndex = 0;
            profileIndex = comboBox1.SelectedIndex;

            backgroundWorker = new BackgroundWorker();
            backgroundWorker.DoWork += OnUploadDoWork;
            backgroundWorker.RunWorkerCompleted += OnUploadCompleted;
            backgroundWorker.RunWorkerAsync();
        }

        private void OnUploadDoWork(object sender, DoWorkEventArgs e)
        {
            e.Result = true;

            //If unselected, then skip it.
            if (dataGridView1.Rows[buildIndex].Cells[0].Value == null)
                return;

            if (!(bool)(dataGridView1.Rows[buildIndex].Cells[0].Value)) //Only check platforms that user has checkbox'd.
                return;

            AddLogInvoke(string.Empty);
            AddLogInvoke(string.Format("Starting upload of '{0}' build:", dataGridView1.Rows[buildIndex].Cells[1].Value));

            //Generate the ignore filter.
            string ignoreFilter = string.Empty;

            if (!string.IsNullOrWhiteSpace(dataGridView1.Rows[buildIndex].Cells[3].Value.ToString()))
            {
                string[] filters = dataGridView1.Rows[buildIndex].Cells[3].Value.ToString().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                for (int i = 0; i < filters.Length; i++)
                {
                    ignoreFilter += string.Format(" --ignore={0}", filters[i]);
                }
            }

            //Generate the arguments.
            string arguments = string.Format("push \"{0}\" {1}/{2}:{3} {4} {5}", dataGridView1.Rows[buildIndex].Cells[2].Value, textBox_username.Text, textBox_gamename.Text, dataGridView1.Rows[buildIndex].Cells[1].Value, profiles[profileIndex].arguments, ignoreFilter);
            AddLogInvoke(arguments);
            AddLogInvoke(string.Empty);

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = BUTLER_EXE;
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.CreateNoWindow = true;
            Process proc = new Process();

            int errorCount = 0;

            try
            {
                proc.StartInfo = startInfo;
                proc.Start();

                while (!proc.StandardOutput.EndOfStream)
                {
                    string line = proc.StandardOutput.ReadLine();
                    AddLogInvoke("    " + line);

                    bool contains = line.IndexOf("itch.io api error", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (contains)
                        errorCount++;
                }
            }
            catch (Exception err)
            {
                AddLogInvoke(string.Format("Error: {0}", err));
                e.Result = false;
                return;
            }

            if (errorCount > 0)
                e.Result = false;
            else
                e.Result = true;
        }

        private void OnUploadCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                AddLog(string.Empty);
                AddLog(string.Format("ERROR: {0}", e.Error));
                listBox1.BackColor = Color.Pink;
                SetButtonsEnabled(true);
                return;
            }

            if (e.Result is bool)
            {
                bool returnvalue = (bool)e.Result;

                if (!returnvalue)
                {
                    //Error.
                    listBox1.BackColor = Color.Pink;
                    AddLog(string.Empty);
                    AddLog("ERROR: upload failed.");
                    SetButtonsEnabled(true);
                    return;
                }
            }

            buildIndex++;

            if (buildIndex < dataGridView1.Rows.Count)
            {
                //More builds to do. Keep iterating.
                backgroundWorker.RunWorkerAsync();
                return;
            }

            //All done.

            SetButtonsEnabled(true);

            TimeSpan delta = DateTime.Now.Subtract(start);
            AddLog(string.Empty);

            string timeStr = string.Empty;
            if (delta.TotalSeconds >= 60)
                timeStr = (string.Format("{0} minutes", Math.Round(delta.TotalMinutes, 1)));
            else
                timeStr = (string.Format("{0} seconds", Math.Round(delta.TotalSeconds, 1)));

            AddLog(string.Format("-- DONE -- (total deployment time: {0})", timeStr));
            AddLog(string.Empty);
            listBox1.BackColor = Color.GreenYellow;
        }

        private void AddLogInvoke(string text)
        {
            MethodInvoker mi = delegate() { AddLog(text); };
            this.Invoke(mi);
        }

        private void copyAllToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;            

            string output = string.Empty;

            foreach (object item in listBox1.Items)
                output += item.ToString() + "\r\n";

            if (string.IsNullOrWhiteSpace(output))
            {
                AddLog(string.Empty);
                AddLog("No log found.");
                return;
            }

            Clipboard.SetText(output);

            AddLog(string.Empty);
            AddLog("Copied entire log to clipboard.");
        }

        private void copyLineToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;

            string output = string.Empty;

            foreach (object item in listBox1.SelectedItems)
            {
                output += item.ToString() + "\r\n";
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                AddLog(string.Empty);
                AddLog("No selected log found.");
                return;
            }

            Clipboard.SetText(output);

            AddLog(string.Empty);
            AddLog("Copied selected log to clipboard.");
        }

        private void clearToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.Items.Clear();
            listBox1.BackColor = Color.White;
        }

        private void logInToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;
            AddLog(string.Empty);
            AddLog("-- BUTLER LOG IN --");
            AddLog("butler login");

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = BUTLER_EXE;
            startInfo.Arguments = "login";
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.CreateNoWindow = true;
            Process proc = new Process();

            try
            {
                proc.StartInfo = startInfo;
                proc.Start();

                while (!proc.StandardOutput.EndOfStream)
                {
                    string line = proc.StandardOutput.ReadLine();
                    AddLog("    " + line);
                }
            }
            catch (Exception err)
            {
                AddLog(string.Format("Error: {0}", err));
            }

            AddLog("-- DONE --");
        }

        private void logOutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;
            AddLog(string.Empty);
            AddLog("-- BUTLER LOG OUT --");
            AddLog("butler logout");

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = BUTLER_EXE;
            startInfo.Arguments = "logout";
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.CreateNoWindow = true;
            Process proc = new Process();

            try
            {
                proc.StartInfo = startInfo;
                proc.Start();

                while (!proc.StandardOutput.EndOfStream)
                {
                    string line = proc.StandardOutput.ReadLine();
                    AddLog("    " + line);
                }
            }
            catch (Exception err)
            {
                AddLog(string.Format("Error: {0}", err));
            }

            AddLog("-- DONE --");
        }

        private void renameCurrentProfileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //Rename profile.
            listBox1.BackColor = Color.White;
            if (comboBox1.SelectedIndex < 0)
            {
                AddLog("Error: can't delete, no profile selected.");
                return;
            }

            string currentProfilename = (comboBox1.Items[comboBox1.SelectedIndex]).ToString();

            string promptValue = Prompt.ShowDialog(string.Format("What would you like to rename '{0}' to?", currentProfilename), "Rename profile");
            promptValue = promptValue.Trim();

            if (string.IsNullOrWhiteSpace(promptValue))
            {
                listBox1.BackColor = Color.Pink;
                AddLog("Error: can't use empty profile name. Rename cancelled.");
                return;
            }

            //Check if it already exists.
            for (int i = 0; i < comboBox1.Items.Count; i++)
            {
                string comboboxText = (comboBox1.Items[i]).ToString();

                if (string.Compare(comboboxText, promptValue, StringComparison.InvariantCultureIgnoreCase) == 0)
                {
                    listBox1.BackColor = Color.Pink;
                    AddLog(string.Format("Error: profile '{0}' already exists.", promptValue));
                    return;
                }
            }

            //Ok, all looks good.
            profiles[comboBox1.SelectedIndex].profilename = promptValue;
            comboBox1.Items[comboBox1.SelectedIndex] = promptValue;
            AddLog(string.Format("Profile '{0}' changed to: '{1}'", currentProfilename, promptValue));
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Blendo itch uploader\nby Brendon Chung\n\nThis is a graphical wrapper around Itch.io's Butler build manager. Use this program to upload projects to Itch.io.\n\nNotes:\n• Put all Butler files in the same folder as this program.\n• Common entries for the Platform field are: windows osx linux\n• Ignore filters allows you to not upload certain files. For example, *.txt will ignore all txt files. Use multiple filters by separating them with a space.", "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void changeCommandlineArgumentsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            
        }

        private void commandlineArgumentsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;

            if (comboBox1.SelectedIndex < 0)
            {
                listBox1.BackColor = Color.Pink;
                AddLog("Error: no profile selected.");
                return;
            }

            string currentProfilename = (comboBox1.Items[comboBox1.SelectedIndex]).ToString();

            string promptValue = Prompt.ShowDialog(string.Format("Command-line arguments for '{0}' profile:", currentProfilename), "Command-line arguments", profiles[comboBox1.SelectedIndex].arguments);
            promptValue = promptValue.Trim();

            profiles[comboBox1.SelectedIndex].arguments = promptValue;

            if (!string.IsNullOrWhiteSpace(promptValue))
            {
                AddLog(string.Format("Command-line argument for '{0}' is now: {1}", currentProfilename, promptValue));
            }
            else
            {
                AddLog("Command-line arguments is now empty.");
            }
        }

        private void buildStatusToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;

            if (comboBox1.SelectedIndex < 0)
            {
                listBox1.BackColor = Color.Pink;
                AddLog("Error: no profile selected.");
                return;
            }

            SetButtonsEnabled(false);

            string currentProfilename = (comboBox1.Items[comboBox1.SelectedIndex]).ToString();

            AddLog(string.Empty);
            AddLog("-- VIEW BUILD STATUS --");
            AddLog(string.Format("Build status for: {0}", currentProfilename));

            string arguments = string.Format("status {0}/{1}", textBox_username.Text, textBox_gamename.Text);

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = BUTLER_EXE;
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.CreateNoWindow = true;
            Process proc = new Process();

            AddLog(arguments);

            try
            {
                proc.StartInfo = startInfo;
                proc.Start();

                while (!proc.StandardOutput.EndOfStream)
                {
                    string line = proc.StandardOutput.ReadLine();
                    AddLog("    " + line);
                }
            }
            catch (Exception err)
            {
                AddLog(string.Format("Error: {0}", err));
                listBox1.BackColor = Color.Pink;
            }

            AddLog("-- DONE --");
            SetButtonsEnabled(true);
        }

        private void upgradeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SetButtonsEnabled(false);

            listBox1.BackColor = Color.White;
            AddLog(string.Empty);
            AddLog("-- UPGRADING BUTLER --");

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = BUTLER_EXE;
            startInfo.Arguments = "upgrade --assume-yes";
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.CreateNoWindow = true;
            Process proc = new Process();

            try
            {
                proc.StartInfo = startInfo;
                proc.Start();

                while (!proc.StandardOutput.EndOfStream)
                {
                    string line = proc.StandardOutput.ReadLine();
                    AddLog("    " + line);
                }
            }
            catch (Exception err)
            {
                listBox1.BackColor = Color.Pink;
                AddLog(string.Format("Error: {0}", err));
            }

            AddLog("-- DONE --");
            SetButtonsEnabled(true);
        }

        private void executeCommandToolStripMenuItem_Click(object sender, EventArgs e)
        {
            listBox1.BackColor = Color.White;

            AddLog(string.Empty);
            AddLog("Opening command prompt.");

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "cmd.exe";
            startInfo.WorkingDirectory = Environment.CurrentDirectory;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = false;
            startInfo.CreateNoWindow = false;

            Process proc = new Process();

            try
            {
                proc.StartInfo = startInfo;
                proc.Start();
            }
            catch (Exception err)
            {
                listBox1.BackColor = Color.Pink;
                AddLog(string.Format("Error: {0}", err));
            }
        }
    }



    //https://stackoverflow.com/questions/5427020/prompt-dialog-in-windows-forms
    public static class Prompt
    {
        public static string ShowDialog(string text, string caption, string defaultText = "")
        {
            Form prompt = new Form()
            {
                Width = 700,
                Height = 150,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = caption,
                StartPosition = FormStartPosition.CenterScreen
            };
            Label textLabel = new Label() { Left = 10, Top = 20, Width=400, Text = text };
            TextBox textBox = new TextBox() { Left = 10, Top = 50, Width = 660 };
            textBox.Text = defaultText;
            textBox.Font = new Font("Lucida Console", 10);
            Button confirmation = new Button() { Text = "Ok", Left = 570, Width = 100, Top = 80, DialogResult = DialogResult.OK };
            confirmation.Click += (sender, e) => { prompt.Close(); };
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.Controls.Add(textLabel);
            prompt.AcceptButton = confirmation;
            prompt.ControlBox = false;

            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";
        }
    }
}
