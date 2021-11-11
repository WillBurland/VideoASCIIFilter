using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;

namespace VideoASCIIFilter
{
    class Program
    {
        private static int threadsFree = 64;
        private static readonly Font consoleFont = new Font("Consolas", 8);

        static void Main()
        {
            string originalFilePath = "";
            string tempFolderPath = Directory.GetCurrentDirectory() + @"\temp\";

            // get video file to convert to ascii
            Thread fileGetThread = new Thread(() => {
                OpenFileDialog openFileDialog = new OpenFileDialog()
                {
                    Filter = "Video Files |*.avi;*.mp4;*.mv4;*.mov;*.wvm",
                    FilterIndex = 2
                };

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    originalFilePath = openFileDialog.FileName;
                }
                else
                {
                    Environment.Exit(0);
                }
            });

            fileGetThread.SetApartmentState(ApartmentState.STA);
            fileGetThread.Start();
            fileGetThread.Join();


            // create a temporary copy of the video file, split it into png frames in a temp folder, then delete the temporary video file
            File.Copy(originalFilePath, Directory.GetCurrentDirectory() + "\\" + Path.GetFileName(originalFilePath).Replace(" ", "_"));
            CreateTempFolder(tempFolderPath);
            ExtractFrames(Path.GetFileName(originalFilePath));
            File.Delete(Directory.GetCurrentDirectory() + "\\" + Path.GetFileName(originalFilePath).Replace(" ", "_"));

            // for each frame, convert it to ascii
            string[] filePaths = Directory.GetFiles(tempFolderPath, "*.png");
            for (int i = 0; i < filePaths.Length ; i++)
            {
                if (threadsFree <= 0)
                {
                    // if there are no availble threads, wait until there is
                    i--;
                    Thread.Sleep(100);
                }
                else
                {
                    // start a new thread to work on a certain frame
                    int temp = i;
                    bool finalFrame = false;

                    if (i == filePaths.Length - 1)
                    {
                        finalFrame = true;
                    }

                    threadsFree--;

                    Console.WriteLine("Starting frame: ({0} / {1})", temp + 1, filePaths.Length);
                    new Thread(() => FrameToASCII(filePaths[temp], finalFrame)).Start();
                }
            }
            Console.WriteLine("Waiting for frames to finish...");

            // wait until the final frame has finished
            while (!File.Exists(tempFolderPath + "\\done"))
            {
                Thread.Sleep(100);
            }
            Console.WriteLine("Done");
            Thread.Sleep(1000); // in case a thread before the final one has taken longer than expected
            AssembleVideo(tempFolderPath, Path.GetFileName(originalFilePath).Replace(" ", "_"), GetFrameRate(originalFilePath), originalFilePath, 20);

            Debug.WriteLine(Directory.GetCurrentDirectory() + "\\output.exe");
            Process.Start("explorer.exe", "/select, \"" + Directory.GetCurrentDirectory() + "\\output.mp4\"");
        }



        public static string MapLightnessToChar(float lightness)
        {
            string charmap = " .:;+=xX$&";
            return charmap[(int)((charmap.Length - 1) * lightness)].ToString();
        }

        public static void CreateTempFolder(string path)
        {
            // create an empty temp folder
            if (Directory.Exists(path))
            {
                DirectoryInfo di = new DirectoryInfo(path);
                foreach (FileInfo file in di.GetFiles())
                {
                    file.Delete();
                }
                foreach (DirectoryInfo dir in di.GetDirectories())
                {
                    dir.Delete(true);
                }
            }
            else
            {
                Directory.CreateDirectory(path);
            }
        }

        public static void ExtractFrames(string fileName)
        {
            // turn video file into images and move them to /temp/
            string path = Directory.GetCurrentDirectory() + @"\bin\ffmpeg.exe";
            string arguments = "-i " + fileName.Replace(" ", "_") + " %08d.png";

            Process.Start(path, arguments).WaitForExit();

            string[] images = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.png", SearchOption.TopDirectoryOnly);

            for (int i = 0; i < images.Length; i++)
            {
                File.Move(images[i], Directory.GetCurrentDirectory() + @"\temp\" + Path.GetFileName(images[i]));
            }
        }

        public static void AssembleVideo(string tempFolder, string fileName, int frameRate, string originalFilePath, int quality)
        {
            // combine frames to video, then add audio

            string directory = Directory.GetCurrentDirectory();
            string[] images = Directory.GetFiles(tempFolder, "*.png", SearchOption.TopDirectoryOnly);

            // move frames to main directory
            for (int i = 0; i < images.Length; i++)
            {
                File.Move(images[i], directory + "\\" + Path.GetFileName(images[i]));
            }
            File.Delete(tempFolder + "\\done");
            Directory.Delete(tempFolder);

            images = Directory.GetFiles(directory, "*.png", SearchOption.TopDirectoryOnly);
            // delete last frame
            File.Delete(images[images.Length - 1]);

            // get size of the output video
            Bitmap temp = new Bitmap(images[0]);
            int width = temp.Width;
            int height = temp.Height;
            string size = width + "x" + height;
            temp.Dispose();

            // ffmpeg exe path
            string path = directory + @"\bin\ffmpeg.exe";

            // combine frames to avi video, with original framerate and quality options
            string arguments = "-framerate " + frameRate + " -pattern_type sequence -i \"%8d.png\" -y -pix_fmt yuv420p -color_trc smpte2084 -color_primaries bt2020 -vcodec libx264 -crf " + quality.ToString() + " -vsync vfr -s " + size + " video.avi";
            Process.Start(path, arguments).WaitForExit();

            // extract audio from original video
            File.Copy(originalFilePath, directory + "\\" + Path.GetFileName(originalFilePath).Replace(" ", "_"));
            arguments = "-i " + fileName + " -vn -acodec copy -y audio.aac";
            Process.Start(path, arguments).WaitForExit();

            // add audio to new ascii video as mp4
            arguments = "-i video.avi -i audio.aac -c:v copy -c:a aac -y output.mp4";
            Process.Start(path, arguments).WaitForExit();

            // remove leftover files
            File.Delete(directory + "\\" + "audio.aac");
            File.Delete(directory + "\\" + "video.avi");
            File.Delete(directory + "\\" + fileName.Replace(" ", "_"));

            // delete video frames
            foreach (string sFile in Directory.GetFiles(directory, "*.png"))
            {
                File.Delete(sFile);
            }
        }

        public static int GetFrameRate(string path)
        {
            // get the frame rate of a video file
            ShellObject obj = ShellObject.FromParsingName(path);
            ShellProperty<uint?> rateProp = obj.Properties.GetProperty<uint?>("System.Video.FrameRate");
            return (int)(rateProp.Value / 1000);
        }

        public static void FrameToASCII(string filepath, bool finalFrame)
        {
            Bitmap bmp = new Bitmap(filepath);
            Bitmap newBmp = new Bitmap(bmp.Width, bmp.Height);

            using (Graphics gfx = Graphics.FromImage(newBmp))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(0, 0, 0)))
            {
                gfx.FillRectangle(brush, 0, 0, newBmp.Width, newBmp.Height);
            }


            Graphics g = Graphics.FromImage(newBmp);
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.None;

            for (int i = 0; i < newBmp.Width; i += 8)
            {
                for (int j = 0; j < newBmp.Height; j += 12)
                {
                    RectangleF rectf = new RectangleF(i, j, newBmp.Width, newBmp.Height);
                    Brush brush = new SolidBrush(bmp.GetPixel(i, j));
                    g.DrawString(MapLightnessToChar(bmp.GetPixel(i, j).GetBrightness()), consoleFont, brush, rectf);
                    brush.Dispose();
                }
            }

            g.Flush();
            g.Dispose();
            bmp.Dispose();

            try
            {
                newBmp.Save(filepath);
            }
            catch
            {
                Debug.WriteLine("ERROR: " + filepath);
            }


            newBmp.Dispose();

            if (finalFrame)
            {
                FileStream done = File.Create(Path.GetDirectoryName(filepath) + "\\done");
                done.Close();
            }

            threadsFree++;
        }

    }
}
