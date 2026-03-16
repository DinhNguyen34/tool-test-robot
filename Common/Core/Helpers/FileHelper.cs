using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common.Core.Helpers
{
    public class FileHelper
    {
        //public static string GetExcelFilePath()
        //{

        //    string filePath = string.Empty;
        //    OpenFileDialog openFileDialog = new OpenFileDialog();
        //    openFileDialog.Filter = "Microsoft Rxcel Worksheet (.xlsx)|*.xlsx";
        //    openFileDialog.Multiselect = false;
        //    if (openFileDialog.ShowDialog() == System.Windows.DialogResult.OK && openFileDialog.FileNames.Length > 0)
        //    {
        //        filePath = openFileDialog.FileName;
        //    }
        //    return filePath;
        //}

        public static string GetFilePathWithAppend(string folderPath, string name, string ext)
        {
            string filePath = Path.Combine(folderPath, string.Format("{0}.{1}", name, ext));

            if (File.Exists(filePath) == false)
            {
                return filePath;
            }
            for (int i = 1; i < 10000; i++)
            {
                filePath = Path.Combine(folderPath, string.Format("{0}_{1}.{2}", name, i, ext));

                if (File.Exists(filePath) == false)
                {
                    return filePath;
                }
            }
            return string.Empty;
        }
        /// <summary>
        /// Only support rename folder in same vol
        /// </summary>
        /// <param name="oldPath"></param>
        /// <param name="newPath"></param>
        /// <returns></returns>
        public static bool RenameFolder(string oldPath, string newPath)
        {
            try
            {
                var oldParent = GetFolderParent(oldPath);
                var newParent = GetFolderParent(newPath);
                if (oldParent.Equals(newParent))
                {
                    Directory.Move(oldPath, $"{oldParent}_temp");
                    Directory.Move($"{oldParent}_temp", newPath);
                }
                else
                {
                    Directory.Move(oldPath, newPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return false;
        }
      
        public static bool RenameFile(string oldPath, string newPath)
        {
            try
            {
                File.Move(oldPath, newPath);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return false;
        }



        public static bool MoveFolder(string oldPath, string newPath)
        {
            try
            {
                Directory.Move(oldPath, newPath);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return false;
        }
        public static void CopyFolder(string sourcePath, string targetPath)
        {
            try
            {
                //Copy all the files & Replaces any files with the same name
                foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.TopDirectoryOnly))
                {
                    File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
                }
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
        }
        public static string GetFileNameWithAppendFollowVSM(string folderPath, string name, string ext)
        {
            string filePath = Path.Combine(folderPath, string.Format("{0}_VSM_SMP_C2_{1}.{2}", DateTime.Now.ToString("yyMMdd"), name, ext));

            if (File.Exists(filePath) == false)
            {
                return filePath;
            }
            for (int i = 1; i < 10000; i++)
            {
                filePath = Path.Combine(folderPath, string.Format("{0}_VSM_SMP_C2_{1}_{2}.{3}", DateTime.Now.ToString("yyMMdd"), name, i, ext));

                if (File.Exists(filePath) == false)
                {
                    return filePath;
                }
            }
            return string.Empty;
        }

        public static string GetFolderParent(string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder) == false)
                {
                    var directory = Directory.GetParent(folder);
                    return directory.FullName;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return string.Empty;
        }

        public static string GetFileNameFollowDate(string folderPath, string name, string ext)
        {
            string filePath = Path.Combine(folderPath, string.Format("{0}_{1}.{2}", DateTime.Now.ToString("yyMMdd"), name, ext));

            if (File.Exists(filePath) == false)
            {
                return filePath;
            }
            for (int i = 1; i < 10000; i++)
            {
                filePath = Path.Combine(folderPath, string.Format("{0}_{1}_{2}.{3}", DateTime.Now.ToString("yyMMdd"), name, i, ext));

                if (File.Exists(filePath) == false)
                {
                    return filePath;
                }
            }
            return string.Empty;
        }


        public static string GetFolderPathFollowDate(string folderPath)
        {
            string filePath = Path.Combine(folderPath, string.Format("{0}", DateTime.Now.ToString("HH-mm-ss")));

            if (File.Exists(filePath) == false)
            {
                return filePath;
            }
            for (int i = 1; i < 10000; i++)
            {
                filePath = Path.Combine(folderPath, string.Format("{0}_{1}_{2}", DateTime.Now.ToString("HH-mm-ss"), i));

                if (File.Exists(filePath) == false)
                {
                    return filePath;
                }
            }
            return string.Empty;
        }

        public static string GetFilePathCopy(string folderPath, string name, string ext)
        {
            string filePath = Path.Combine(folderPath, string.Format("{0}_Copy.{1}", name, ext));

            if (File.Exists(filePath) == false)
            {
                return filePath;
            }
            for (int i = 1; i < 10000; i++)
            {
                filePath = Path.Combine(folderPath, string.Format("{0}_Copy_{1}.{2}", name, i, ext));

                if (File.Exists(filePath) == false)
                {
                    return filePath;
                }
            }
            return string.Empty;
        }
        public static string GetFilePathFollowTimeWithDot(string folderPath, string name)
        {
            var index = name.LastIndexOf(".");
            string ext = string.Empty;
            if (index != -1)
            {
                ext = index < name.Length - 1 ? name.Substring(index + 1) : string.Empty;

            }

            string filePath = string.IsNullOrWhiteSpace(ext) == false ? Path.Combine(folderPath, string.Format("{0}_{1}.{2}", DateTime.Now.ToString("yyMMdd_HHmmss"), name.Substring(0, index), ext))
                                                        : Path.Combine(folderPath, string.Format("{0}_{1}", DateTime.Now.ToString("yyMMdd_HHmmss"), name));

            if (File.Exists(filePath) == false)
            {
                return filePath;
            }
            for (int i = 1; i < 10000; i++)
            {
                filePath = string.IsNullOrWhiteSpace(ext) == false ? Path.Combine(folderPath, string.Format("{0}_{1}_{2}.{3}", DateTime.Now.ToString("yyMMdd_HHmmss"), name.Substring(0, index), i, ext))
                                                        : Path.Combine(folderPath, string.Format("{0}_{1}_{2}", DateTime.Now.ToString("yyMMdd_HHmmss"), name, i)); ;

                if (File.Exists(filePath) == false)
                {
                    return filePath;
                }
            }

            return string.Empty;
        }
        public static string GetFolderPathFollowDateTime(string folderPath, string name)
        {
            string logName = string.IsNullOrWhiteSpace(name) ? string.Format("{0}_Log", DateTime.Now.ToString("yyMMdd_HH-mm-ss")) : name;
            string path = Path.Combine(folderPath, logName);

            if (Directory.Exists(path) == false)
            {
                return path;
            }
            for (int i = 1; i < 10000; i++)
            {
                path = Path.Combine(folderPath, string.Format("{0}_{1}", logName, i));

                if (Directory.Exists(path) == false)
                {
                    return path;
                }
            }
            return string.Empty;
        }

        public static string GetFileNameFollowDateTime(string folderPath, string name, string ext)
        {
            string filePath = Path.Combine(folderPath, string.Format("{0}_{1}.{2}", name, DateTime.Now.ToString("yyMMdd_HHmmss"), ext));

            if (File.Exists(filePath) == false)
            {
                return filePath;
            }
            for (int i = 1; i < 10000; i++)
            {
                filePath = Path.Combine(folderPath, string.Format("{0}_{1}_{2}.{3}", name, DateTime.Now.ToString("yyMMdd_HHmmss"), i, ext));

                if (File.Exists(filePath) == false)
                {
                    return filePath;
                }
            }
            return string.Empty;
        }
        public static string GetFolderOrFileName(string folderPath, string ignoreStr = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folderPath) == false)
                {
                    var arr = folderPath.Split('\\');
                    if (arr?.Length > 1)
                    {
                        var folderName = arr[arr.Length - 1];
                        if (folderName != null)
                        {
                            if (ignoreStr != null)
                            {
                                var lastIndex = folderName.LastIndexOf(ignoreStr);
                                if (lastIndex != -1)
                                    folderName = folderName.Substring(0, lastIndex);
                            }
                            return folderName;
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return string.Empty;
        }
        public static string GetFileNameWithoutExtention(string filePath, string extension = ".tc")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) == false)
                {
                    var arr = filePath.Split('\\');
                    if (arr?.Length > 1)
                    {
                        var fileName = arr[arr.Length - 1].Replace(extension, "");
                        if (string.IsNullOrWhiteSpace(fileName) == false)
                            return fileName;
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return string.Empty;
        }


        public static bool FolderIsExist(string folderLog)
        {
            return Directory.Exists(folderLog) == true;
        }

        public static bool DeleteFile(string file)
        {
            try
            {
                if (file == null || file.Length <= 0 || !File.Exists(file))
                    return false;
                File.Delete(file);
                return true;
            }
            catch
            {
                //LogHelper.Error("File could not be deleted!  " + file);
            }
            return false;
        }

        public static bool DeleteFolder(string path)
        {
            try
            {
                if (path == null || path.Length <= 0 || !Directory.Exists(path))
                    return false;
                Directory.Delete(path, true);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error("Folder could not be deleted!  " + path);
            }
            return false;

        }

        public static bool FileIsExist(string? file)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(file) == false && File.Exists(file))
                    return true;
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return false;
        }

        public static FileInfo GetFileInfo(string file)
        {
            try
            {
                FileInfo fi = new FileInfo(file);
                return fi;
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return null;
        }
        public static bool FileHaveSizeBigger(string filePath, int size = 0)
        {
            try
            {
                var fileinfor = GetFileInfo(filePath);
                if (FileHelper.FileIsExist(filePath) && fileinfor != null && fileinfor.Length > size)
                    return true;
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return false;
        }
        public static bool CreateFolder(string folder)
        {
            try
            {
                if (folder == null || folder.Length <= 0 || Directory.Exists(folder))
                    return false;
                Directory.CreateDirectory(folder);
                return true;
            }
            catch
            {
                LogHelper.Error("Can not create folder!  " + folder);
            }
            return false;
        }


        public static List<string> GetFilesInFolder(string folderPath, string regexPatent, SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            try
            {
                DirectoryInfo d = new DirectoryInfo(folderPath);
                FileInfo[] files = d.GetFiles("*.*", searchOption);
                List<string> listFiles = new List<string>();
                for (int i = 0; i < files.Length; i++)
                {//_server_RPXAE2F21MFC00991_push_2022-03-26-10-16-47,10733

                    if (files[i].Name.Contains("_server_"))
                    {

                    }
                    if (string.IsNullOrWhiteSpace(regexPatent))
                    {
                        listFiles.Add(files[i].FullName);
                    }
                    else if (System.Text.RegularExpressions.Regex.IsMatch(files[i].Name, regexPatent))
                    {
                        listFiles.Add(files[i].FullName);
                    }
                }

                return listFiles;
            }
            catch
            {
                LogHelper.Error("Can not create folder!  " + folderPath);
            }
            return null;
        }

        public static List<string> GetFoldersInFolder(string folderPath)
        {
            try
            {
                DirectoryInfo d = new DirectoryInfo(folderPath);
                DirectoryInfo[] folders = d.GetDirectories("*", SearchOption.TopDirectoryOnly);
                List<string> listFiles = new List<string>();
                for (int i = 0; i < folders.Length; i++)
                {
                    listFiles.Add(folders[i].FullName);

                }

                return listFiles;
            }
            catch
            {
                LogHelper.Error("Can not get folders in " + folderPath);
            }
            return null;
        }


        public bool AwaitFile(string filePath, int timeout)
        {
            //Your File
            var file = new FileInfo(filePath);

            //While File is not accesable because of writing process
            int times = timeout / 200;
            for (int i = 0; i < times; i++)
            {
                Thread.Sleep(200);
                if (IsFileLocked(file) == false)
                    return true;
            }
            return false;
            //File is available here
        }

        private bool IsFileLocked(FileInfo file)
        {
            try
            {
                using (FileStream stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                //the file is unavailable because it is:
                //still being written to
                //or being processed by another thread
                //or does not exist (has already been processed)
                return true;
            }

            //file is not locked
            return false;
        }

        
        public static string GetTextFromFileText(string filePath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                return content;
            }
            catch
            {
                LogHelper.Error("Can not openfile " + filePath);
            }
            return null;
        }

        public static void CreateEmptyFileIfNotExist(string filePath)
        {
            try
            {
                if (FileHelper.FileIsExist(filePath) == false)
                    using (StreamWriter w = File.AppendText(filePath))
                    {
                        w.Flush();
                    }
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
        }
        public static async Task<bool> WriteFileAsync(string folderPath, string filePath, string output)
        {
            try
            {
                if (Directory.Exists(folderPath) == false)
                {
                    Directory.CreateDirectory(folderPath);
                }
                await File.WriteAllTextAsync(filePath, output);
                return true;

            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return false;
        }

        public static async Task<bool> WriteFileAsync(string filePath, string output)
        {
            try
            {
                await File.WriteAllTextAsync(filePath, output);
                return true;

            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return false;
        }

       
        public static bool CopyFile(string originalPath, string newFilePath)
        {
            try
            {
                File.Copy(originalPath, newFilePath, true);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return false;
        }

        public static bool TryMoveFile(string originalPath, string newFilePath, int number)
        {
            for (int i = 0; i < number; i++)
            {
                try
                {
                    File.Move(originalPath, newFilePath, true);
                    return true;
                }
                catch (Exception ex)
                {
                    LogHelper.Exception(ex);
                }
                Thread.Sleep(100);
            }
            return false;
        }
        public static bool WriteFile(string filePath, string output)
        {
            try
            {
                File.WriteAllText(filePath, output);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return false;
        }

       
        public static string CreateFolderAndParrent(string rootFolder, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) == false)
            {
                var fileName = GetFolderOrFileName(filePath);
                string subFolderStr = filePath.Replace(rootFolder, "");
                if (string.IsNullOrWhiteSpace(subFolderStr) == false && string.IsNullOrWhiteSpace(fileName) == false)
                {
                    var arrs = subFolderStr.Split(new char[]{
                        '\\','/'});
                    if (arrs.Length > 1)
                    {
                        string folderPath;
                        for (int i = 0; i < arrs.Length - 1; i++)
                        {
                            if (string.IsNullOrWhiteSpace(arrs[i]) == false)
                            {
                                var path = createFolder(rootFolder, arrs[i]);
                                if (string.IsNullOrWhiteSpace(path))
                                {
                                    return string.Empty;
                                }
                                else
                                {
                                    rootFolder = path;
                                }
                            }
                        }
                        return createFolder(rootFolder, fileName);
                    }
                    else if (arrs.Length == 1)
                    {
                        return createFolder(rootFolder, fileName);
                    }
                }
            }
            return string.Empty;
        }

        private static string createFolder(string rootFolder, string fileName)
        {
            string folderPath = Path.Combine(rootFolder, fileName);
            if (Directory.Exists(folderPath) == false && FileHelper.CreateFolder(folderPath) == false)
            {

                LogHelper.Debug("File is exist");
                return string.Empty;
            }

            return folderPath;
        }

        public static string GetLastestFolder(string folderPath)
        {
            try
            {
                if (FolderIsExist(folderPath))
                {
                    var folders = Directory.GetDirectories(folderPath, "*.*", SearchOption.TopDirectoryOnly);
                    DateTime dateTime;
                    DateTime? preDateTime = null;
                    var outFolder = string.Empty;
                    foreach (var folder in folders)
                    {
                        var folderName = GetFolderOrFileName(folder);
                        if (folderName.Contains("_FAIL") || folderName.Contains("_PASS") || folderName.Contains("_WARN"))
                        {
                            folderName = folderName.Replace("_FAIL", "").Replace("_PASS", "").Replace("_WARN", "");
                        }

                        if (DateTime.TryParseExact(folderName, "yyMMdd_HHmmss",
                                                            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dateTime))
                        {
                            if (preDateTime == null)
                            {
                                preDateTime = dateTime;
                                outFolder = folder;
                            }
                            else if (DateTime.Compare(dateTime, (DateTime)preDateTime) >= 0)
                            {
                                preDateTime = dateTime;
                                if (folderName.Contains("_FAIL") || folderName.Contains("_PASS") || folderName.Contains("_WARN"))
                                    outFolder = folder;
                            }
                        }
                    }
                    if (preDateTime != null)
                    {
                        return outFolder;
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return string.Empty;
        }

        public static bool MergeTextFile(string newPath, List<string> listFiles, bool revert, int numberFile)
        {
            try
            {
                const int chunkSize = 2 * 1024; // 2KB
                int maxCount = System.Math.Min(numberFile, listFiles.Count);
                using (var output = File.Create(newPath))
                {
                    if (revert == false)
                    {
                        for (int i = 0; i < maxCount; i++)
                        {
                            using (var input = File.OpenRead(listFiles[i]))
                            {
                                var buffer = new byte[chunkSize];
                                int bytesRead;
                                while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    output.Write(buffer, 0, bytesRead);
                                }
                            }
                        }
                    }
                    else
                    {
                        for (int i = maxCount - 1; i >= 0; i--)
                        {
                            using (var input = File.OpenRead(listFiles[i]))
                            {
                                var buffer = new byte[chunkSize];
                                int bytesRead;
                                while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    output.Write(buffer, 0, bytesRead);
                                }
                            }
                        }

                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return false;
        }


        public static string[]? GetALlFolders(string folderPath, SearchOption searchOption = SearchOption.AllDirectories)
        {
            try
            {
                if (FileHelper.FolderIsExist(folderPath))
                {
                    var directories = Directory.GetDirectories(folderPath, "*", searchOption);

                    return directories;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return null;
        }

        public static bool HasFile(string folderPath, string extentions)//"*.tc"
        {
            try
            {
                if (FileHelper.FolderIsExist(folderPath))
                {
                    var files = Directory.GetFiles(folderPath, extentions, SearchOption.TopDirectoryOnly);
                    if (files?.Length > 0)
                        return true;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return false;
        }

       

        // Method to get all file paths recursively
        public static IEnumerable<string> GetAllFilePaths(string folderPath)
        {
            // Get all files in the current directory
            foreach (string file in Directory.GetFiles(folderPath))
            {
                yield return file;
            }
        }


        #region Get FilePath from Description
       
        public static string FormatPathFromDescriptionTC(string desciption)
        {
            if (string.IsNullOrWhiteSpace(desciption) == false)
            {
                string subPath = string.Empty;
                desciption = desciption.Replace("/", "\\");
                if (FileHelper.FileIsExist(desciption))
                {
                    return GetFileNameWithoutExtention(desciption);
                }
                else
                {
                    if (desciption[0] == '/' || desciption[0] == '\\')
                    {
                        desciption = desciption.Substring(1);
                    }

                    return desciption.Replace(".tc", "");
                }
            }
            return string.Empty;
        }
        #endregion

        #region readFile
        public static List<string> ReadLinesFromFile(string filePath)
        {
            List<string> lines = new List<string>();
            try
            {
                using (StreamReader r = new StreamReader(new FileStream(filePath, FileMode.Open)))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        lines.Add(line);
                    }

                }
                return lines;
            }
            catch (Exception ex)
            {
                LogHelper.Exception(ex);
            }
            return lines;

        }

        public static string GetExistAppFile(string filePath)
        {
            if (FileHelper.FileIsExist(filePath))
                return filePath;
            else
            {
                var path = Path.Combine(CommonApplicationUtilities.AppFolder, filePath);
                return path;
            }
        }
        #endregion
    }
}
