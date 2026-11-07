using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WallScape
{
    internal class SettingsForm : Form
    {
        private static readonly string[] AnimationValues =
        {
            "circle", "slide_top", "diamond", "split", "none"
        };

        private static readonly string[] AnimationLabels =
        {
            "Circle", "Slide from top", "Diamond", "Split", "None"
        };

        private readonly List<string> _folders;
        private readonly ListBox _folderList;
        private readonly TextBox _hotkey;
        private readonly ComboBox _animation;
        private readonly NumericUpDown _duration;
        private readonly CheckBox _autostart;

        public SettingsForm(Config config, Icon icon)
        {
            _folders = new List<string>(config.Folders);

            Text = "WallScape settings";
            if (icon != null)
                Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(470, 418);
            Font = new Font("Segoe UI", 9f);

            Label foldersLabel = new Label();
            foldersLabel.Text = "Wallpaper folders (scanned recursively)";
            foldersLabel.AutoSize = true;
            foldersLabel.Location = new Point(16, 14);
            Controls.Add(foldersLabel);

            _folderList = new ListBox();
            _folderList.Location = new Point(16, 36);
            _folderList.Size = new Size(438, 132);
            _folderList.HorizontalScrollbar = true;
            Controls.Add(_folderList);

            Button add = new Button();
            add.Text = "Add folder\u2026";
            add.Location = new Point(16, 176);
            add.Size = new Size(110, 28);
            add.Click += OnAddFolder;
            Controls.Add(add);

            Button remove = new Button();
            remove.Text = "Remove";
            remove.Location = new Point(132, 176);
            remove.Size = new Size(90, 28);
            remove.Click += OnRemoveFolder;
            Controls.Add(remove);

            Label hotkeyLabel = new Label();
            hotkeyLabel.Text = "Hotkey";
            hotkeyLabel.AutoSize = true;
            hotkeyLabel.Location = new Point(16, 222);
            Controls.Add(hotkeyLabel);

            _hotkey = new TextBox();
            _hotkey.Location = new Point(120, 219);
            _hotkey.Size = new Size(150, 24);
            _hotkey.Text = config.Hotkey;
            Controls.Add(_hotkey);

            Label hotkeyHint = new Label();
            hotkeyHint.Text = "e.g. Alt+E, Ctrl+Alt+W";
            hotkeyHint.AutoSize = true;
            hotkeyHint.ForeColor = SystemColors.GrayText;
            hotkeyHint.Location = new Point(280, 222);
            Controls.Add(hotkeyHint);

            Label animationLabel = new Label();
            animationLabel.Text = "Animation";
            animationLabel.AutoSize = true;
            animationLabel.Location = new Point(16, 258);
            Controls.Add(animationLabel);

            _animation = new ComboBox();
            _animation.DropDownStyle = ComboBoxStyle.DropDownList;
            _animation.Location = new Point(120, 255);
            _animation.Size = new Size(150, 24);
            _animation.Items.AddRange(AnimationLabels);
            _animation.SelectedIndex = IndexOfAnimation(config.Animation);
            Controls.Add(_animation);

            Label durationLabel = new Label();
            durationLabel.Text = "Transition";
            durationLabel.AutoSize = true;
            durationLabel.Location = new Point(16, 294);
            Controls.Add(durationLabel);

            _duration = new NumericUpDown();
            _duration.Location = new Point(120, 291);
            _duration.Size = new Size(100, 24);
            _duration.Minimum = 100;
            _duration.Maximum = 5000;
            _duration.Increment = 100;
            _duration.Value = Math.Max(100, Math.Min(5000, config.DurationMs));
            Controls.Add(_duration);

            Label msLabel = new Label();
            msLabel.Text = "ms";
            msLabel.AutoSize = true;
            msLabel.Location = new Point(226, 294);
            Controls.Add(msLabel);

            _autostart = new CheckBox();
            _autostart.Text = "Start WallScape with Windows";
            _autostart.AutoSize = true;
            _autostart.Location = new Point(16, 332);
            _autostart.Checked = config.AutoStart;
            Controls.Add(_autostart);

            Label note = new Label();
            note.Text = "Supported: PNG, JPG, BMP, GIF, TIF. Thumbnails are cached in the cache folder next to the exe.";
            note.AutoSize = false;
            note.ForeColor = SystemColors.GrayText;
            note.Location = new Point(16, 360);
            note.Size = new Size(438, 18);
            Controls.Add(note);

            Button ok = new Button();
            ok.Text = "OK";
            ok.Location = new Point(274, 382);
            ok.Size = new Size(86, 28);
            ok.Click += OnOk;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Cancel";
            cancel.Location = new Point(368, 382);
            cancel.Size = new Size(86, 28);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;

            RefreshFolders();
        }

        private static int IndexOfAnimation(string value)
        {
            for (int i = 0; i < AnimationValues.Length; i++)
            {
                if (AnimationValues[i] == value)
                    return i;
            }
            return 0;
        }

        private void RefreshFolders()
        {
            _folderList.BeginUpdate();
            _folderList.Items.Clear();
            for (int i = 0; i < _folders.Count; i++)
                _folderList.Items.Add(_folders[i]);
            _folderList.EndUpdate();
        }

        private void OnAddFolder(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select a folder that contains wallpapers";
                dialog.ShowNewFolderButton = true;
                try
                {
                    dialog.SelectedPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                }
                catch
                {
                }
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                string folder = dialog.SelectedPath;
                if (string.IsNullOrEmpty(folder))
                    return;

                foreach (string existing in _folders)
                {
                    if (string.Equals(existing, folder, StringComparison.OrdinalIgnoreCase))
                        return;
                }
                _folders.Add(folder);
                RefreshFolders();
                _folderList.SelectedIndex = _folderList.Items.Count - 1;
            }
        }

        private void OnRemoveFolder(object sender, EventArgs e)
        {
            int index = _folderList.SelectedIndex;
            if (index < 0 || index >= _folders.Count)
                return;
            _folders.RemoveAt(index);
            RefreshFolders();
        }

        private void OnOk(object sender, EventArgs e)
        {
            string hotkey = _hotkey.Text.Trim();
            uint modifiers;
            uint virtualKey;
            if (!Hotkey.TryParse(hotkey, out modifiers, out virtualKey))
            {
                MessageBox.Show(this, "Enter a hotkey with at least one modifier, for example Alt+E.",
                    "WallScape", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _hotkey.Focus();
                _hotkey.SelectAll();
                return;
            }

            if (_folders.Count == 0)
            {
                DialogResult answer = MessageBox.Show(this,
                    "No wallpaper folders are configured. Add one now?",
                    "WallScape", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer == DialogResult.Yes)
                {
                    OnAddFolder(sender, e);
                    return;
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        public void ApplyTo(Config config)
        {
            config.Folders.Clear();
            config.Folders.AddRange(_folders);
            config.Hotkey = _hotkey.Text.Trim();
            config.Animation = AnimationValues[Math.Max(0, _animation.SelectedIndex)];
            config.DurationMs = (int)_duration.Value;
            config.AutoStart = _autostart.Checked;
        }
    }
}
