using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImageEditor
{
    public partial class FrmImageEditor : Form
    {

        private Bitmap originalBitmap;
        private List<string> imagePaths = new List<string>();
        private int currentIndex = -1;
        private string outputFolder = "";

        private bool isSelecting = false;
        private Point startPoint;
        private Rectangle selectionRect = Rectangle.Empty;

        private DateTime lastUiUpdate = DateTime.MinValue;
        private const int UiUpdateIntervalMs = 16; // ~60 FPS (33 yaparsan ~30 FPS)
        private int lastCropW = -1;
        private int lastCropH = -1;


        public FrmImageEditor()
        {
            InitializeComponent();
            this.DoubleBuffered = true;

        }

        private void btnFolder_Click(object sender, EventArgs e)
        {

            // using var yok -> klasik using bloğu
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                if (fbd.ShowDialog() != DialogResult.OK)
                    return;

                string folder = fbd.SelectedPath;

                // Kaydedilecek klasör: seçilen klasör\Kirpilmis
                outputFolder = Path.Combine(folder, "Kirpilmis");

                if (!Directory.Exists(outputFolder))
                    Directory.CreateDirectory(outputFolder);

                HashSet<string> exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".jpg", ".jpeg", ".png", ".bmp"
                };

                imagePaths = Directory.GetFiles(folder)
                    .Where(p => exts.Contains(Path.GetExtension(p)))
                    .OrderBy(p => p)
                    .ToList();


                if (imagePaths.Count == 0)
                {
                    MessageBox.Show("Bu klasörde .jpg/.jpeg/.png/.bmp bulunamadı!");
                    return;
                }

                currentIndex = -1;
                LoadNextImage();
            }
        }

        private void btnSkip_Click(object sender, EventArgs e)
        {
            LoadNextImage();
        }

        private void btnSaveNext_Click(object sender, EventArgs e)
        {
            if (originalBitmap == null)
                return;

            if (selectionRect.Width < 5 || selectionRect.Height < 5)
            {
                MessageBox.Show("Önce kırpma alanı seçmelisin.");
                return;
            }

            Rectangle cropRect = TranslateSelectionToImageRect(pictureBox1, originalBitmap, selectionRect);
            if (cropRect == Rectangle.Empty)
            {
                MessageBox.Show("Seçim geçersiz (görüntü alanının dışında olabilir).");
                return;
            }

            Bitmap cropped = null;
            try
            {
                cropped = originalBitmap.Clone(cropRect, originalBitmap.PixelFormat);

                string srcPath = imagePaths[currentIndex];
                string name = Path.GetFileNameWithoutExtension(srcPath);
                string ext = Path.GetExtension(srcPath);

                string outPath = Path.Combine(outputFolder, name + "_Kirpilmis" + ext);

                cropped.Save(outPath);
            }
            finally
            {
                if (cropped != null) cropped.Dispose();
            }

            LoadNextImage();
        }

        private void LoadNextImage()
        {
            selectionRect = Rectangle.Empty;
            isSelecting = false;

            currentIndex++;

            if (imagePaths == null || currentIndex >= imagePaths.Count)
            {
                pictureBox1.Image = null;

                if (originalBitmap != null)
                {
                    originalBitmap.Dispose();
                    originalBitmap = null;
                }

                lblStatus.Text = "Bitti.";
                return;
            }

            if (originalBitmap != null)
            {
                originalBitmap.Dispose();
                originalBitmap = null;
            }

            originalBitmap = new Bitmap(imagePaths[currentIndex]);
            pictureBox1.Image = originalBitmap;

            lblStatus.Text =
                (currentIndex + 1).ToString() + "/" + imagePaths.Count.ToString()
                + " - (" + originalBitmap.Width + " x " + originalBitmap.Height + ")"
                + " - " + Path.GetFileName(imagePaths[currentIndex]);


            pictureBox1.Invalidate();

            lblCropSize.Text = "Yeni Boyut: -";
        }

        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            if (selectionRect != Rectangle.Empty)
            {
                using (Pen pen = new Pen(Color.Lime, 2))
                {
                    e.Graphics.DrawRectangle(pen, selectionRect);
                }
            }
        }

        private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            if (originalBitmap == null) return;

            isSelecting = true;
            startPoint = e.Location;
            selectionRect = new Rectangle(e.Location, new Size(0, 0));
            pictureBox1.Invalidate();

            lastCropW = -1;
            lastCropH = -1;
            lastUiUpdate = DateTime.MinValue;

        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isSelecting) return;

            int x = Math.Min(startPoint.X, e.X);
            int y = Math.Min(startPoint.Y, e.Y);
            int w = Math.Abs(startPoint.X - e.X);
            int h = Math.Abs(startPoint.Y - e.Y);

            selectionRect = new Rectangle(x, y, w, h);

            // Çok sık Invalidate yapma (UI'yı boğar)
            DateTime now = DateTime.Now;
            if ((now - lastUiUpdate).TotalMilliseconds < UiUpdateIntervalMs)
                return;

            lastUiUpdate = now;

            pictureBox1.Invalidate(); // 60 fps civarı çiz

            if (originalBitmap != null && selectionRect.Width > 0 && selectionRect.Height > 0)
            {
                Rectangle cropRect = TranslateSelectionToImageRect(pictureBox1, originalBitmap, selectionRect);

                if (cropRect != Rectangle.Empty)
                {
                    // Label'ı sadece gerçekten değiştiyse güncelle
                    if (cropRect.Width != lastCropW || cropRect.Height != lastCropH)
                    {
                        lastCropW = cropRect.Width;
                        lastCropH = cropRect.Height;
                        lblCropSize.Text = "Yeni Boyut: " + lastCropW + " x " + lastCropH + " px";
                    }
                }
                else
                {
                    if (lastCropW != -1 || lastCropH != -1)
                    {
                        lastCropW = -1;
                        lastCropH = -1;
                        lblCropSize.Text = "Yeni Boyut: -";
                    }
                }
            }

        }

        private void pictureBox1_MouseUp(object sender, MouseEventArgs e)
        {
            isSelecting = false;
            pictureBox1.Invalidate();
        }

        private Rectangle TranslateSelectionToImageRect(PictureBox pb, Bitmap img, Rectangle sel)
        {
            Rectangle displayRect = GetImageDisplayRectangle(pb, img);

            Rectangle intersect = Rectangle.Intersect(sel, displayRect);
            if (intersect.Width <= 0 || intersect.Height <= 0)
                return Rectangle.Empty;

            float scaleX = (float)img.Width / (float)displayRect.Width;
            float scaleY = (float)img.Height / (float)displayRect.Height;

            int x = (int)((intersect.X - displayRect.X) * scaleX);
            int y = (int)((intersect.Y - displayRect.Y) * scaleY);
            int w = (int)(intersect.Width * scaleX);
            int h = (int)(intersect.Height * scaleY);

            if (x < 0) x = 0;
            if (y < 0) y = 0;

            if (x >= img.Width) x = img.Width - 1;
            if (y >= img.Height) y = img.Height - 1;

            if (w < 1) w = 1;
            if (h < 1) h = 1;

            if (x + w > img.Width) w = img.Width - x;
            if (y + h > img.Height) h = img.Height - y;

            return new Rectangle(x, y, w, h);
        }

        private Rectangle GetImageDisplayRectangle(PictureBox pb, Image img)
        {
            if (pb.SizeMode != PictureBoxSizeMode.Zoom)
                return pb.ClientRectangle;

            float imageRatio = (float)img.Width / (float)img.Height;
            float boxRatio = (float)pb.ClientSize.Width / (float)pb.ClientSize.Height;

            int drawWidth, drawHeight;

            if (imageRatio > boxRatio)
            {
                drawWidth = pb.ClientSize.Width;
                drawHeight = (int)(pb.ClientSize.Width / imageRatio);
            }
            else
            {
                drawHeight = pb.ClientSize.Height;
                drawWidth = (int)(pb.ClientSize.Height * imageRatio);
            }

            int x = (pb.ClientSize.Width - drawWidth) / 2;
            int y = (pb.ClientSize.Height - drawHeight) / 2;

            return new Rectangle(x, y, drawWidth, drawHeight);
        }

        private void FrmImageEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (originalBitmap != null)
            {
                originalBitmap.Dispose();
                originalBitmap = null;
            }
            Application.Exit();
        }

    }
}
