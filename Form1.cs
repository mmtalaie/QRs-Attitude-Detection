using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;
using AForge;
using AForge.Imaging.Filters;
using AForge.Math;
using AForge.Math.Geometry;
using AForge.Video;
using AForge.Video.DirectShow;
using AForge.Vision.GlyphRecognition;
using Xna3DViewer;

namespace QRs
{
    public partial class Form1 : Form
    {
        #region kalmanFilter
        KalmanFilter KARoll;
        KalmanFilter KAPitch;
        KalmanFilter KAYaw;


        double LastRoll;
        double LastPitch;
        double LastYaw;

        #endregion


        #region Configuration Option Names
        private const string activeDatabaseOption = "ActiveDatabase";
        private const string mainFormXOption = "MainFormX";
        private const string mainFormYOption = "MainFormY";
        private const string mainFormWidthOption = "MainFormWidth";
        private const string mainFormHeightOption = "MainFormHeight";
        private const string mainFormStateOption = "MainFormState";
        private const string mainSplitterOption = "MainSplitter";
        private const string glyphSizeOption = "GlyphSize";
        private const string focalLengthOption = "FocalLength";
        private const string detectFocalLengthOption = "DetectFocalLength";
        private const string autoDetectFocalLengthOption = "AutoDetectFocalLength";
        float pich1 = 0, pich2 = 0, roll1 = 0, roll2 = 0;
        #endregion
        private AugmentedRealityForm arForm = null;
        private const string ErrorBoxTitle = "Error";
        private string activeGlyphDatabaseName = null;
        private bool autoDetectFocalLength = true;
        private ImageList glyphsImageList = new ImageList();
        private GlyphDatabases glyphDatabases = new GlyphDatabases();
        private GlyphDatabase activeGlyphDatabase = null;
        private GlyphImageProcessor imageProcessor = new GlyphImageProcessor();
        private Stopwatch stopWatch = null;
        private object sync = new object();
        string glyphNameInEditor = string.Empty;
        AForge.Point pointA;
        AForge.Point pointB;
        AForge.Point pointC;
        AForge.Point pointD;
        AForge.Point[] points;
        // model points
        private AForge.Math.Vector3[] modelPoints = new AForge.Math.Vector3[4];
        // camera's focal length
        private float focalLength;
        // estimated transformation

        private float modelRadius;
        AForge.Math.Matrix3x3 matrixA;


        float yaw = 500, pitch = 500, roll = 500;
        FileStream fs;
        FileStream fs_matrix;
        bool Write = false;

        public Form1()
        {
            //matrixA = new Matrix3x3()
            //{
            //    V00 = 0.0F,
            //    V01 = 0.0F,
            //    V02 = 0.0F,
            //    V10 = 0.0F,
            //    V11 = 0.0F,
            //    V12 = 0.0F,
            //    V20 = 0.0F,
            //    V21 = 0.0F,
            //    V22 = 0.0F
            //};


            InitializeComponent();
            glyphsImageList.ImageSize = new Size(32, 32);
            glyphList.LargeImageList = glyphsImageList;
            Label.CheckForIllegalCrossThreadCalls = false;

        }
        private void ShowErrorBox(string message)
        {
            MessageBox.Show(message, ErrorBoxTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        private void glyphCollectionsList_AfterLabelEdit(object sender, LabelEditEventArgs e)
        {
            if (e.Label != null)
            {
                string newName = e.Label.Trim();

                if (newName == string.Empty)
                {
                    ShowErrorBox("Collection name cannot be emtpy.");
                    e.CancelEdit = true;
                    return;
                }
                else
                {
                    string oldName = glyphCollectionsList.Items[e.Item].Text;

                    if (oldName != newName)
                    {
                        if (glyphDatabases.GetDatabaseNames().Contains(newName))
                        {
                            ShowErrorBox("A collection with such name already exists.");
                            e.CancelEdit = true;
                            return;
                        }

                        glyphDatabases.RenameGlyphDatabase(oldName, newName);

                        // update name of active database if it was renamed
                        if (activeGlyphDatabaseName == oldName)
                            activeGlyphDatabaseName = newName;

                        if (newName != e.Label)
                        {
                            glyphCollectionsList.Items[e.Item].Text = newName;
                            e.CancelEdit = true;
                        }
                    }
                    else
                    {
                        e.CancelEdit = true;
                    }
                }
            }
        }


        private void videoSourcePlayer_NewFrame(object sender, ref Bitmap image)
        {
            Graphics g = Graphics.FromImage(image);
            if (activeGlyphDatabase != null)
            {
                if (image.PixelFormat == PixelFormat.Format8bppIndexed)
                {
                    // convert image to RGB if it is grayscale
                    GrayscaleToRGB filter = new GrayscaleToRGB();

                    Bitmap temp = filter.Apply(image);
                    image.Dispose();
                    image = temp;
                }

                lock (sync)
                {
                    List<ExtractedGlyphData> glyphs = imageProcessor.ProcessImage(image);

                    if (arForm != null)
                    {
                        List<VirtualModel> modelsToDisplay = new List<VirtualModel>();

                        foreach (ExtractedGlyphData glyph in glyphs)
                        {
                            if ((glyph.RecognizedGlyph != null) &&
                                 (glyph.RecognizedGlyph.UserData != null) &&
                                 (glyph.RecognizedGlyph.UserData is GlyphVisualizationData) &&
                                 (glyph.IsTransformationDetected))
                            {
                                modelsToDisplay.Add(new VirtualModel(
                                    ((GlyphVisualizationData)glyph.RecognizedGlyph.UserData).ModelName,
                                    glyph.TransformationMatrix,
                                    imageProcessor.GlyphSize));
                            }
                        }

                        arForm.UpdateScene(image, modelsToDisplay);
                    }
                    if (glyphs.Count == 4)
                    {

                        foreach (var item in glyphs)
                        {
                            try
                            {
                                switch (item.RecognizedGlyph.Name)
                                {
                                    case "A":
                                        pointA = new AForge.Point((item.RecognizedQuadrilateral.ToArray()[0].X +
                                                                   item.RecognizedQuadrilateral.ToArray()[1].X +
                                                                   item.RecognizedQuadrilateral.ToArray()[2].X +
                                                                   item.RecognizedQuadrilateral.ToArray()[3].X) / 4,
                                                                  (item.RecognizedQuadrilateral.ToArray()[0].Y +
                                                                   item.RecognizedQuadrilateral.ToArray()[1].Y +
                                                                   item.RecognizedQuadrilateral.ToArray()[2].Y +
                                                                   item.RecognizedQuadrilateral.ToArray()[3].Y) / 4);
                                        break;
                                    case "B":
                                        pointB = new AForge.Point((item.RecognizedQuadrilateral.ToArray()[0].X +
                                                                   item.RecognizedQuadrilateral.ToArray()[1].X +
                                                                   item.RecognizedQuadrilateral.ToArray()[2].X +
                                                                   item.RecognizedQuadrilateral.ToArray()[3].X) / 4,
                                                                  (item.RecognizedQuadrilateral.ToArray()[0].Y +
                                                                   item.RecognizedQuadrilateral.ToArray()[1].Y +
                                                                   item.RecognizedQuadrilateral.ToArray()[2].Y +
                                                                   item.RecognizedQuadrilateral.ToArray()[3].Y) / 4);
                                        break;
                                    case "C":
                                        pointC = new AForge.Point((item.RecognizedQuadrilateral.ToArray()[0].X +
                                                                   item.RecognizedQuadrilateral.ToArray()[1].X +
                                                                   item.RecognizedQuadrilateral.ToArray()[2].X +
                                                                   item.RecognizedQuadrilateral.ToArray()[3].X) / 4,
                                                                  (item.RecognizedQuadrilateral.ToArray()[0].Y +
                                                                   item.RecognizedQuadrilateral.ToArray()[1].Y +
                                                                   item.RecognizedQuadrilateral.ToArray()[2].Y +
                                                                   item.RecognizedQuadrilateral.ToArray()[3].Y) / 4);
                                        break;
                                    case "D":
                                        pointD = new AForge.Point((item.RecognizedQuadrilateral.ToArray()[0].X +
                                                                   item.RecognizedQuadrilateral.ToArray()[1].X +
                                                                   item.RecognizedQuadrilateral.ToArray()[2].X +
                                                                   item.RecognizedQuadrilateral.ToArray()[3].X) / 4,
                                                                  (item.RecognizedQuadrilateral.ToArray()[0].Y +
                                                                   item.RecognizedQuadrilateral.ToArray()[1].Y +
                                                                   item.RecognizedQuadrilateral.ToArray()[2].Y +
                                                                   item.RecognizedQuadrilateral.ToArray()[3].Y) / 4);
                                        break;
                                    default:
                                        break;
                                }

                            }
                            catch (Exception)
                            {

                            }
                        }

                        points = new AForge.Point[]
                        {
                            new AForge.Point() { X = pointA.X, Y = pointA.Y },
                            new AForge.Point() { X = pointB.X, Y = pointB.Y },
                            new AForge.Point() { X = pointC.X, Y = pointC.Y },
                            new AForge.Point() { X = pointD.X, Y = pointD.Y }
                        };
                        Pen pen = new Pen(Color.Green, 3);
                        IntPoint[] temp = new IntPoint[] {
                            new IntPoint() { X = Convert.ToInt32(pointA.X), Y = Convert.ToInt32(pointA.Y) },
                            new IntPoint() { X = Convert.ToInt32(pointB.X), Y = Convert.ToInt32(pointB.Y) },
                            new IntPoint() { X = Convert.ToInt32(pointC.X), Y = Convert.ToInt32(pointC.Y) },
                            new IntPoint() { X = Convert.ToInt32(pointD.X), Y = Convert.ToInt32(pointD.Y) }
                        };
                        // highlight border
                        g.DrawPolygon(pen, ToPointsArray(temp.ToList<IntPoint>()));
                        EstimatePose(points);
                    }
                }
            }
        }
        private System.Drawing.Point[] ToPointsArray(List<IntPoint> points)
        {
            int count = points.Count;
            System.Drawing.Point[] pointsArray = new System.Drawing.Point[count];

            for (int i = 0; i < count; i++)
            {
                pointsArray[i] = new System.Drawing.Point(points[i].X, points[i].Y);
            }

            return pointsArray;
        }
        private bool useCoplanarPosit = true;
        DateTime gtime;
        Matrix3x3 rotationMatrix = new Matrix3x3();
        Matrix3x3 bestRotationMatrix = new Matrix3x3();
        Matrix3x3 alternateRotationMatrix = new Matrix3x3();
        Vector3 translationVector = new Vector3();
        Vector3 bestTranslationVector = new Vector3();
        Vector3 alternateTranslationVector = new Vector3();




        private void EstimatePose(AForge.Point[] inputPoints)
        {
            try
            {
                lock (sync)
                {
                    Vector3 modelCenter = new Vector3(
                   (modelPoints[0].X + modelPoints[1].X + modelPoints[2].X + modelPoints[3].X) / 4,
                   (modelPoints[0].Y + modelPoints[1].Y + modelPoints[2].Y + modelPoints[3].Y) / 4,
                   (modelPoints[0].Z + modelPoints[1].Z + modelPoints[2].Z + modelPoints[3].Z) / 4);



                    modelRadius = 0;

                    float estimatedYaw;
                    float estimatedPitch;
                    float estimatedRoll;

                    foreach (Vector3 modelPoint in modelPoints)
                    {
                        float distanceToCenter = (modelPoint - modelCenter).Norm;
                        if (distanceToCenter > modelRadius)
                        {
                            modelRadius = distanceToCenter;
                        }
                    }

                    Matrix3x3 matrix = new Matrix3x3()
                    {
                        V00 = float.Parse(tbx00.Text),
                        V01 = float.Parse(tbx01.Text),
                        V02 = float.Parse(tbx02.Text),
                        V10 = float.Parse(tbx10.Text),
                        V11 = float.Parse(tbx11.Text),
                        V12 = float.Parse(tbx12.Text),
                        V20 = float.Parse(tbx20.Text),
                        V21 = float.Parse(tbx21.Text),
                        V22 = float.Parse(tbx22.Text)
                    };

                    if (!useCoplanarPosit)
                    {
                        Posit posit = new Posit(modelPoints, focalLength);
                        posit.EstimatePose(inputPoints, out rotationMatrix, out translationVector);

                        //bestPoseButton.Visible = alternatePoseButton.Visible = false;
                    }
                    else
                    {
                        CoplanarPosit coposit = new CoplanarPosit(modelPoints, focalLength);
                        coposit.EstimatePose(inputPoints, out rotationMatrix, out translationVector);

                        bestRotationMatrix = coposit.BestEstimatedRotation;
                        bestTranslationVector = coposit.BestEstimatedTranslation;
                        // translationVector = new Vector3() { X = bestTranslationVector.X, Y = -bestTranslationVector.Z, Z = -bestTranslationVector.Y };
                        alternateRotationMatrix = coposit.AlternateEstimatedRotation;
                        alternateTranslationVector = coposit.AlternateEstimatedTranslation;


                    }
                    // zarb
                    if (chbuseit.Checked == true)
                    {
                        //TODO :: create orginal rotation matrix matrix
                        rotationMatrix = matrixA * rotationMatrix;
                        if (!chbafter.Checked)
                            rotationMatrix = matrix * rotationMatrix;
                        else
                            rotationMatrix = rotationMatrix * matrix;
                    }

                    rotationMatrix = rotationMatrix.Transpose();
                    estimatedPitch = (float)Math.Asin(-rotationMatrix.V02);
                    estimatedYaw = (float)Math.Atan2(rotationMatrix.V01, rotationMatrix.V00);
                    estimatedRoll = (float)Math.Atan2(rotationMatrix.V12, rotationMatrix.V22);

                    if (checkBox1.Checked)
                    {
                        yaw = estimatedYaw = Convert.ToSingle(KAYaw.Output(estimatedYaw * (float)(180.0 / Math.PI)));
                        pitch = estimatedPitch = Convert.ToSingle(KAPitch.Output(estimatedPitch * (float)(180.0 / Math.PI)));
                        roll = estimatedRoll = Convert.ToSingle(KARoll.Output(estimatedRoll * (float)(180.0 / Math.PI)));

                    }
                    else
                    {
                        yaw = estimatedYaw = Convert.ToSingle(estimatedYaw * (float)(180.0 / Math.PI));
                        pitch = estimatedPitch = Convert.ToSingle(estimatedPitch * (float)(180.0 / Math.PI));
                        roll = estimatedRoll = Convert.ToSingle(estimatedRoll * (float)(180.0 / Math.PI));
                    }

                    // TO DOO
                    label1.Text = string.Format("A :: Rotation: (yaw(Z)={0}, pitch(y)={1}, roll(X)={2})",
                                      Convert.ToInt32(estimatedYaw), Convert.ToInt32(estimatedPitch), Convert.ToInt32(estimatedRoll));

                    estimatedTransformationMatrixControl1.SetMatrix(
                                                              Matrix4x4.CreateTranslation(translationVector) *
                                                             Matrix4x4.CreateFromRotation(rotationMatrix));



                }
            }
            catch (Exception)
            {

            }
        }

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            NewGlyphCollectionForm newCollectionForm = new NewGlyphCollectionForm(glyphDatabases.GetDatabaseNames());

            if (newCollectionForm.ShowDialog() == DialogResult.OK)
            {
                string name = newCollectionForm.CollectionName;
                int size = newCollectionForm.GlyphSize;

                GlyphDatabase db = new GlyphDatabase(size);

                try
                {
                    glyphDatabases.AddGlyphDatabase(name, db);

                    // add new item to list view
                    ListViewItem lvi = glyphCollectionsList.Items.Add(name);
                    lvi.SubItems.Add(string.Format("{0}x{1}", size, size));
                    lvi.Name = name;
                }
                catch
                {
                    ShowErrorBox(string.Format("A glyph database with the name '{0}' already exists.", name));
                }
            }
        }

        private void renameToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (glyphCollectionsList.SelectedIndices.Count == 1)
            {
                glyphCollectionsList.Items[glyphCollectionsList.SelectedIndices[0]].BeginEdit();
            }
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (glyphCollectionsList.SelectedIndices.Count == 1)
            {
                string selecteItem = glyphCollectionsList.SelectedItems[0].Text;

                if (selecteItem == activeGlyphDatabaseName)
                {
                    ActivateGlyphDatabase(null);
                }

                glyphDatabases.RemoveGlyphDatabase(selecteItem);
                glyphCollectionsList.Items.Remove(glyphCollectionsList.SelectedItems[0]);
            }
        }
        private void ActivateGlyphDatabase(string name)
        {
            ListViewItem lvi;

            // deactivate previous database
            if (activeGlyphDatabase != null)
            {
                lvi = GetListViewItemByName(glyphCollectionsList, activeGlyphDatabaseName);

                if (lvi != null)
                {
                    Font font = new Font(lvi.Font, FontStyle.Regular);
                    lvi.Font = font;
                }
            }

            // activate new database
            activeGlyphDatabaseName = name;

            if (name != null)
            {
                try
                {
                    activeGlyphDatabase = glyphDatabases[name];

                    lvi = GetListViewItemByName(glyphCollectionsList, name);

                    if (lvi != null)
                    {
                        Font font = new Font(lvi.Font, FontStyle.Bold);
                        lvi.Font = font;
                    }
                }
                catch
                {
                }
            }
            else
            {
                activeGlyphDatabase = null;
            }

            // set the database to image processor ...
            imageProcessor.GlyphDatabase = activeGlyphDatabase;
            // ... and show it to user
            RefreshListOfGlyps();
        }

        private ListViewItem GetListViewItemByName(ListView lv, string name)
        {
            try
            {
                return lv.Items[name];
            }
            catch
            {
                return null;
            }
        }

        private void RefreshListOfGlyps()
        {
            // clear list view and its image list
            glyphList.Items.Clear();
            glyphsImageList.Images.Clear();

            if (activeGlyphDatabase != null)
            {
                // update image list first
                foreach (Glyph glyph in activeGlyphDatabase)
                {
                    // create icon for the glyph first
                    glyphsImageList.Images.Add(glyph.Name, CreateGlyphIcon(glyph));

                    // create glyph's list view item
                    ListViewItem lvi = glyphList.Items.Add(glyph.Name);
                    lvi.ImageKey = glyph.Name;
                }
            }
        }

        private Bitmap CreateGlyphIcon(Glyph glyph)
        {
            return CreateGlyphImage(glyph, 32);
        }
        private Bitmap CreateGlyphImage(Glyph glyph, int width)
        {
            Bitmap bitmap = new Bitmap(width, width, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            int cellSize = width / glyph.Size;
            int glyphSize = glyph.Size;

            for (int i = 0; i < width; i++)
            {
                int yCell = i / cellSize;

                for (int j = 0; j < width; j++)
                {
                    int xCell = j / cellSize;

                    if ((yCell >= glyphSize) || (xCell >= glyphSize))
                    {
                        // set pixel to transparent if it outside of the glyph
                        bitmap.SetPixel(j, i, Color.Transparent);
                    }
                    else
                    {
                        // set pixel to black or white depending on glyph value
                        bitmap.SetPixel(j, i,
                            (glyph.Data[yCell, xCell] == 0) ? Color.Black : Color.White);
                    }
                }
            }

            return bitmap;
        }
        private void RefreshListOfGlyphDatabases()
        {
            glyphCollectionsList.Items.Clear();

            List<string> dbNames = glyphDatabases.GetDatabaseNames();

            foreach (string name in dbNames)
            {
                GlyphDatabase db = glyphDatabases[name];
                ListViewItem lvi = glyphCollectionsList.Items.Add(name);
                lvi.Name = name;

                lvi.SubItems.Add(string.Format("{0}x{1}", db.Size, db.Size));
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // estimatedTransformationMatrixControl1 = new QRs.Controls.MatrixControl();
            double rA = 1;
            double rH = 1;
            double rQ = 0.990;//noise
            double rR = 300;
            double rP = 0.1;
            double rx = 0;
            KARoll = new KalmanFilter(rA, rH, rQ, rR, rP, rx);

            double pA = 1;
            double pH = 1;
            double pQ = 0.990;//noise
            double pR = 300;
            double pP = 0.1;
            double px = 0;
            KAPitch = new KalmanFilter(pA, pH, pQ, pR, pP, px);

            double yA = 1;
            double yH = 1;
            double yQ = 0.900;//noise
            double yR = 100;
            double yP = 0.1;
            double yx = 0;
            KAYaw = new KalmanFilter(yA, yH, yQ, yR, yP, yx);

            estimatedTransformationMatrixControl1.Clear();
            // load configuratio
            Configuration config = Configuration.Instance;
            modelPointInit();
            try
            {
                focalLength = int.Parse(textBox1.Text);
            }
            catch (Exception)
            {

            }
            if (config.Load(glyphDatabases))
            {
                RefreshListOfGlyphDatabases();
                ActivateGlyphDatabase(config.GetConfigurationOption(activeDatabaseOption));

                try
                {
                    Location = new System.Drawing.Point(
                        int.Parse(config.GetConfigurationOption(mainFormXOption)),
                        int.Parse(config.GetConfigurationOption(mainFormYOption)));

                    Size = new Size(
                        int.Parse(config.GetConfigurationOption(mainFormWidthOption)),
                        int.Parse(config.GetConfigurationOption(mainFormHeightOption)));

                    WindowState = (FormWindowState)Enum.Parse(typeof(FormWindowState),
                        config.GetConfigurationOption(mainFormStateOption));

                    splitContainer1.SplitterDistance = int.Parse(config.GetConfigurationOption(mainSplitterOption));

                    autoDetectFocalLength = bool.Parse(config.GetConfigurationOption(autoDetectFocalLengthOption));
                    imageProcessor.GlyphSize = float.Parse(config.GetConfigurationOption(glyphSizeOption));
                    if (!autoDetectFocalLength)
                    {
                        imageProcessor.CameraFocalLength = float.Parse(config.GetConfigurationOption(focalLengthOption));
                    }
                }
                catch
                {
                }
            }
        }

        private void modelPointInit()
        {
            modelPoints = new Vector3[]
                    {

                    //new Vector3() { X = -73.0F,Y =-73.0F  ,Z =0},
                    //new Vector3() { X = 73.0F, Y =-73.0F  ,Z =0},
                    //new Vector3() { X = 73.0F, Y = 73.0F  ,Z =0},
                    //new Vector3() { X = -73.0F,Y = 73.0F  ,Z =0}

                    new Vector3() { X =-176.5F, Y =-176.5F  ,Z =0},
                    new Vector3() { X =+176.5F, Y =-176.5F  ,Z =0},
                    new Vector3() { X =+176.5F, Y =+176.5F  ,Z =0},
                    new Vector3() { X =-176.5F, Y =+176.5F  ,Z =0}



                    };
            modelPoint1xBox.Text = modelPoints[0].X.ToString();
            modelPoint1yBox.Text = modelPoints[0].Y.ToString();
            modelPoint1zBox.Text = modelPoints[0].Z.ToString();

            modelPoint2xBox.Text = modelPoints[1].X.ToString();
            modelPoint2yBox.Text = modelPoints[1].Y.ToString();
            modelPoint2zBox.Text = modelPoints[1].Z.ToString();

            modelPoint3xBox.Text = modelPoints[2].X.ToString();
            modelPoint3yBox.Text = modelPoints[2].Y.ToString();
            modelPoint3zBox.Text = modelPoints[2].Z.ToString();

            modelPoint4xBox.Text = modelPoints[3].X.ToString();
            modelPoint4yBox.Text = modelPoints[3].Y.ToString();
            modelPoint4zBox.Text = modelPoints[3].Z.ToString();
        }

        private bool CheckGlyphData(byte[,] glyphData)
        {
            if (activeGlyphDatabase != null)
            {
                int rotation;
                Glyph recognizedGlyph = activeGlyphDatabase.RecognizeGlyph(glyphData, out rotation);

                if ((recognizedGlyph != null) && (recognizedGlyph.Name != glyphNameInEditor))
                {
                    ShowErrorBox("The database already contains a glyph which looks the same as it is or after rotation.");
                    return false;
                }
            }

            return true;
        }

        private void newGlyphToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (activeGlyphDatabase != null)
            {
                // create new glyph ...
                Glyph glyph = new Glyph(string.Empty, activeGlyphDatabase.Size);
                glyphNameInEditor = string.Empty;
                // ... and pass it the glyph editting form
                EditGlyphForm glyphForm = new EditGlyphForm(glyph, activeGlyphDatabase.GetGlyphNames());
                glyphForm.Text = "New Glyph";

                // set glyph data checking handler
                glyphForm.SetGlyphDataCheckingHandeler(new GlyphDataCheckingHandeler(CheckGlyphData));

                if (glyphForm.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        lock (sync)
                        {
                            // add glyph to active database
                            activeGlyphDatabase.Add(glyph);
                        }

                        // create an icon for it
                        glyphsImageList.Images.Add(glyph.Name, CreateGlyphIcon(glyph));

                        // add it to list view
                        ListViewItem lvi = glyphList.Items.Add(glyph.Name);
                        lvi.ImageKey = glyph.Name;
                    }
                    catch
                    {
                        ShowErrorBox(string.Format("A glyph with the name '{0}' already exists in the database.", glyph.Name));
                    }
                }
            }
        }

        private void editGlyphToolStripMenuItem_Click(object sender, EventArgs e)
        {
            EditSelectedGlyph();
        }


        private void EditSelectedGlyph()
        {
            if ((activeGlyphDatabase != null) && (glyphList.SelectedIndices.Count != 0))
            {
                // get selected item and it glyph ...
                ListViewItem lvi = glyphList.SelectedItems[0];
                Glyph glyph = (Glyph)activeGlyphDatabase[lvi.Text].Clone();
                glyphNameInEditor = glyph.Name;
                // ... and pass it to the glyph editting form
                EditGlyphForm glyphForm = new EditGlyphForm(glyph, activeGlyphDatabase.GetGlyphNames());
                glyphForm.Text = "Edit Glyph";

                // set glyph data checking handler
                glyphForm.SetGlyphDataCheckingHandeler(new GlyphDataCheckingHandeler(CheckGlyphData));

                if (glyphForm.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        // replace glyph in the database
                        lock (sync)
                        {
                            activeGlyphDatabase.Replace(glyphNameInEditor, glyph);
                        }

                        lvi.Text = glyph.Name;

                        // temporary remove icon from the list item
                        lvi.ImageKey = null;

                        // remove old icon and add new one
                        glyphsImageList.Images.RemoveByKey(glyphNameInEditor);
                        glyphsImageList.Images.Add(glyph.Name, CreateGlyphIcon(glyph));

                        // restore item's icon
                        lvi.ImageKey = glyph.Name;
                    }
                    catch
                    {
                        ShowErrorBox(string.Format("A glyph with the name '{0}' already exists in the database.", glyph.Name));
                    }
                }
            }
        }

        private void focalLen_Click(object sender, EventArgs e)
        {
            focalLength = float.Parse(textBox1.Text);
        }

        private void Copy_Click(object sender, EventArgs e)
        {
            try
            {
                var mtrx1 = estimatedTransformationMatrixControl1.GetMatricx();
                matrixA = new Matrix3x3()
                {

                    // V00 = mtrx1.V00,
                    //V01 = mtrx1.V10,
                    // V02 = mtrx1.V20,
                    //V10 = mtrx1.V01,
                    // V11 = mtrx1.V11,
                    // V12 = mtrx1.V21,
                    // V20 = mtrx1.V02,
                    // V21 = mtrx1.V12,
                    //V22 = mtrx1.V22


                    V00 = mtrx1.V00,
                    V01 = mtrx1.V01,
                    V02 = mtrx1.V02,
                    V10 = mtrx1.V10,
                    V11 = mtrx1.V11,
                    V12 = mtrx1.V12,
                    V20 = mtrx1.V20,
                    V21 = mtrx1.V21,
                    V22 = mtrx1.V22
                };
            }
            catch (Exception)
            {

                //matrixA = new Matrix3x3()
                //{
                //    V00 = 0.0F,
                //    V01 = 0.0F,
                //    V02 = 0.0F,
                //    V10 = 0.0F,
                //    V11 = 0.0F,
                //    V12 = 0.0F,
                //    V20 = 0.0F,
                //    V21 = 0.0F,
                //    V22 = 0.0F
                //};
            }
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            IVideoSource videoSource = videoSourcePlayer.VideoSource;
            if (videoSource != null)
            {
                int framesReceived = videoSource.FramesReceived;
                if (stopWatch == null)
                {
                    stopWatch = new Stopwatch();
                    stopWatch.Start();
                }
                else
                {
                    stopWatch.Stop();

                    float fps = 1000.0f * framesReceived / stopWatch.ElapsedMilliseconds;
                    fpsLabel.Text = fps.ToString("F2") + " fps";

                    stopWatch.Reset();
                    stopWatch.Start();
                }
            }
        }

        private void button5_Click(object sender, EventArgs e)
        {
            gtime = DateTime.Now;
            if (!Write)
            {
                Write = true;
                button3.Enabled = button5.Enabled = false;
                string time = System.DateTime.Now.ToString("yyyy-MM-dd hh-mm-ss-ff");
                fs = File.Create(time + ".txt");
                byte[] title = new UTF8Encoding(true).GetBytes("Time                  A:yaw,pitch,roll    B:yaw,pitch,roll    C:yaw,pitch,roll\r\n");
                fs.Write(title, 0, title.Length);
                timer1.Start();
                button3.Enabled = button5.Enabled = true;
                button3.Text = button5.Text = "Stop";
            }
            else
            {
                timer1.Stop();
                Write = false;
                button3.Enabled = button5.Enabled = false;
                fs.Close();
                button3.Enabled = button5.Enabled = true;
                button3.Text = button5.Text = "Write and Plot";
            }
            tabControl1.SelectedIndex = 1;
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            int h = DateTime.Now.Hour - gtime.Hour;
            int m = DateTime.Now.Minute - gtime.Minute;
            int s = DateTime.Now.Second - gtime.Second;
            int ms = DateTime.Now.Millisecond - gtime.Millisecond;
            float time = h * 3600 + m * 60 + s + ms / 1000.0F;


            if (roll != 500) graph(rollChart, "A Roll", roll, Color.Blue); else graph(rollChart, "A Roll", LastRoll, Color.Blue); ;
            if (pitch != 500) graph(pitchChart, "A Pich", pitch, Color.Blue); else graph(pitchChart, "A Pich", LastPitch, Color.Blue);
            if (yaw != 500) graph(yawChart, "A Yaw", yaw, Color.Blue); else graph(yawChart, "A Yaw", LastYaw, Color.Blue);




            byte[] title = new UTF8Encoding(true).GetBytes(string.Format("{0}    {1},{2},{3}    \r\n", time, yaw, pitch, roll));
            fs.Write(title, 0, title.Length);
            Func<double, double, double> isEqual500 = (x, y) => { if (x == 500.0) return y; else return x; };
            LastRoll = isEqual500(roll, LastRoll);
            LastYaw = isEqual500(yaw, LastYaw);
            LastPitch = isEqual500(pitch, LastPitch);

            yaw = 500; pitch = 500; roll = 500;

        }
        protected void graph(Chart c, string name, double value, Color clr)
        {
            if (c.Series[0].Points.Count == 0)
            {
                c.Series[0].Points.AddXY(0, 0);
            }
            Series ser = new Series(name);
            ser.ChartType = SeriesChartType.Line;
            for (int i = 0; i <= c.Series[0].Points.Count; i++)
            {
                if (i == c.Series[0].Points.Count)
                {
                    ser.Points.Add(new DataPoint(i, value));
                }
                else
                {
                    ser.Points.Add(new DataPoint(i, c.Series[0].Points[i].YValues));
                }
            }
            c.Series.Clear();
            ser.Color = clr;
            c.Series.Add(ser);
            c.ChartAreas[0].RecalculateAxesScale();

        }

        private void button1_Click(object sender, EventArgs e)
        {
            VideoCaptureDeviceForm form = new VideoCaptureDeviceForm();
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                OpenVideoSource(form.VideoDevice);
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                // create video source
                FileVideoSource fileSource = new FileVideoSource(openFileDialog.FileName);

                // open it
                OpenVideoSource(fileSource);
            }
        }
        private void OpenVideoSource(IVideoSource source)
        {
            this.Cursor = Cursors.WaitCursor;
            imageProcessor.Reset();

            videoSourcePlayer.SignalToStop();
            videoSourcePlayer.WaitForStop();

            videoSourcePlayer.VideoSource = new AsyncVideoSource(source);
            videoSourcePlayer.Start();
            stopWatch = null;
            timer.Start();

            this.Cursor = Cursors.Default;
        }
        private void modelPointBox_Leave(object sender, EventArgs e)
        {
            GetCoordinateValue((TextBox)sender);
        }
        private void GetCoordinateValue(TextBox textBox)
        {
            int tag = int.Parse((string)textBox.Tag);
            int pointIndex = tag / 10;
            int coordinateIndex = tag % 10;
            float coordinateValue, oldValue = 0;

            textBox.Text = textBox.Text.Trim();

            // try parsing the coordinate value
            if (float.TryParse(textBox.Text, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out coordinateValue))
            {
                switch (coordinateIndex)
                {
                    case 0:
                        oldValue = modelPoints[pointIndex].X;
                        modelPoints[pointIndex].X = coordinateValue;
                        break;
                    case 1:
                        oldValue = modelPoints[pointIndex].Y;
                        modelPoints[pointIndex].Y = coordinateValue;
                        break;
                    case 2:
                        oldValue = modelPoints[pointIndex].Z;
                        modelPoints[pointIndex].Z = coordinateValue;
                        break;
                }
                errorProvider.Clear();

                if (oldValue != coordinateValue)
                {
                    //ClearEstimation();
                }
            }
            else
            {
                Label pointLabel = (Label)modelPointsGroupBox.Controls[string.Format("modelPoint{0}Label", pointIndex + 1)];

                errorProvider.SetError(pointLabel, string.Format("Failed parsing {0} coordinate",
                    (coordinateIndex == 0) ? "X" : ((coordinateIndex == 1) ? "Y" : "Z")));

                textBox.Text = string.Empty;
            }
        }



        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            //
            if (fs != null)
                fs.Close();
            Configuration config = Configuration.Instance;

            if (WindowState != FormWindowState.Minimized)
            {
                if (WindowState != FormWindowState.Maximized)
                {
                    config.SetConfigurationOption(mainFormXOption, Location.X.ToString());
                    config.SetConfigurationOption(mainFormYOption, Location.Y.ToString());
                    config.SetConfigurationOption(mainFormWidthOption, Width.ToString());
                    config.SetConfigurationOption(mainFormHeightOption, Height.ToString());
                }
                config.SetConfigurationOption(mainFormStateOption, WindowState.ToString());
                config.SetConfigurationOption(mainSplitterOption, splitContainer1.SplitterDistance.ToString());
            }

            config.SetConfigurationOption(activeDatabaseOption, activeGlyphDatabaseName);

            config.SetConfigurationOption(autoDetectFocalLengthOption, autoDetectFocalLength.ToString());
            config.SetConfigurationOption(focalLengthOption, imageProcessor.CameraFocalLength.ToString());
            config.SetConfigurationOption(glyphSizeOption, imageProcessor.GlyphSize.ToString());

            try
            {
                config.Save(glyphDatabases);
            }
            catch (IOException ex)
            {
                ShowErrorBox("Failed saving confguration file.\r\n\r\n" + ex.Message);
            }

            if (videoSourcePlayer.VideoSource != null)
            {
                videoSourcePlayer.SignalToStop();
                videoSourcePlayer.WaitForStop();
            }
            //

        }

        private void button4_Click(object sender, EventArgs e)
        {
            var mtrx1 = estimatedTransformationMatrixControl1.GetMatricx();
     
                fs_matrix = File.Create("matrix.txt");
            
            byte[] title = new UTF8Encoding(true).GetBytes(string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}",
            mtrx1.V00,
            mtrx1.V01,
            mtrx1.V02,
            mtrx1.V10,
            mtrx1.V11,
            mtrx1.V12,
            mtrx1.V20,
            mtrx1.V21,
            mtrx1.V22));
            fs_matrix.Write(title, 0, title.Length);
            fs_matrix.Close();
        }

        private void button6_Click(object sender, EventArgs e)
        {
            if(File.Exists("matrix.txt"))
            {
                string smatrix = File.ReadAllText("matrix.txt");
                var cels = smatrix.Split(',');
                
                matrixA = new Matrix3x3()
                {
                 V00 =float.Parse(cels[0]),
                 V01 =float.Parse(cels[1]),
                 V02 =float.Parse(cels[2]),
                 V10 =float.Parse(cels[3]),
                 V11 =float.Parse(cels[4]),
                 V12 =float.Parse(cels[5]),
                 V20 =float.Parse(cels[6]),
                 V21 =float.Parse(cels[7]),
                 V22 =float.Parse(cels[8])
                 };
            }
        }




    }
}
