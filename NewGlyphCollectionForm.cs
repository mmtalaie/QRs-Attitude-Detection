using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace QRs
{
    public partial class NewGlyphCollectionForm : Form
    {
        private List<string> forbiddenNames;
        public string CollectionName
        {
            get { return nameBox.Text.Trim(); }
        }

        public int GlyphSize
        {
            get { return sizeCombo.SelectedIndex + 5; }
        }
        public NewGlyphCollectionForm(List<string> existingNames)
        {
            InitializeComponent();
            sizeCombo.SelectedIndex = 0;
            forbiddenNames = existingNames;
        }

        private void nameBox_TextChanged(object sender, EventArgs e)
        {
            string name = CollectionName;

            okButton.Enabled = false;

            if (name.Length == 0)
            {
                errorProvider.SetError(nameBox, "Glyph database name can not be empty");
                return;
            }
            else if (forbiddenNames.IndexOf(name) != -1)
            {
                errorProvider.SetError(nameBox, "A glyph database with such name already exists");
                return;
            }

            errorProvider.Clear();
            okButton.Enabled = true;
        }
    }
}
