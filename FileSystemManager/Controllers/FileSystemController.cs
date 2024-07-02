using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace FileSystemManager.Controllers
{
    public class FileSystemController : Controller
    {
        private readonly string rootPath;
        private readonly IConfiguration configuration;
        private readonly ApplicationDbContext _context;

        public FileSystemController(IConfiguration configuration, ApplicationDbContext context)
        {
            this.configuration = configuration;
            _context = context;
            rootPath = configuration["PhotoDirectory"] ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
        }
        

        public IActionResult Index(string path = "", int page = 1, int pageSize = 10, string searchQuery = "")
        {
            var sanitizedPath = string.IsNullOrEmpty(path) ? "" : path.TrimStart('/');
            var currentPath = Path.Combine(rootPath, sanitizedPath);

            if (!Directory.Exists(currentPath))
            {
                return NotFound();
            }

            List<DirectoryInfo> directories;
            List<FileInfo> files;

            // If searchQuery not empty, recursively search all directories.
            if (!string.IsNullOrEmpty(searchQuery))
            {
                // Perform recursive search
                directories = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                                       .Select(d => new DirectoryInfo(d))
                                       .Where(d => d.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                                       .ToList();

                files = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories)
                                 .Select(f => new FileInfo(f))
                                 .Where(f => f.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                                 .ToList();
            }
            else
            {
                directories = Directory.GetDirectories(currentPath)
                               .Select(d => new DirectoryInfo(d))
                               .OrderByDescending(d => d.CreationTime) // Sort by creation time to ensure new folders are on top
                               .ToList();

                files = Directory.GetFiles(currentPath)
                                 .Select(f => new FileInfo(f))
                                 .OrderByDescending(f => f.CreationTime) // Sort by creation time to ensure new files are on top
                                 .ToList();
            }

            var breadcrumbs = GenerateBreadcrumbs(sanitizedPath);
            
            var items = directories.Cast<FileSystemInfo>().Concat(files.Cast<FileSystemInfo>()).ToList();
            var paginatedItems = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();




            ViewBag.CurrentPath = sanitizedPath;
            ViewBag.Items = paginatedItems;
            ViewBag.ParentPath = string.IsNullOrEmpty(sanitizedPath) ? "" : Path.GetDirectoryName(sanitizedPath.Replace("/", "\\"))?.Replace("\\", "/") ?? "";
            ViewBag.Breadcrumbs = breadcrumbs;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)items.Count / pageSize);
            ViewBag.SearchQuery = searchQuery;
            ViewBag.RootPath = rootPath;

            return View();
        }

        [HttpGet]
        public IActionResult CreateDirectory(string path = "")
        {
            ViewBag.CurrentPath = path;
            return View();
        }

        [HttpPost]
        public IActionResult CreateDirectory(string directoryName, string path)
        {
            var sanitizedPath = string.IsNullOrEmpty(path) ? "" : path.TrimStart('/');
            var currentPath = Path.Combine(rootPath, sanitizedPath);
            var newPath = Path.Combine(currentPath, directoryName);

            if (Directory.Exists(newPath))
            {
                return Conflict(new { suggestedName = GetUniqueDirectoryName(newPath) });
            }

            Directory.CreateDirectory(newPath);
            return RedirectToAction("Index", new { path });
        }

        [HttpGet]
        public IActionResult UploadFile(string path = "")
        {
            ViewBag.CurrentPath = path;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UploadFile(IFormFile file, string path, string issuedBy, DateTime? expiryDate)
        {
            if (file != null && file.Length > 0)
            {
                var sanitizedPath = string.IsNullOrEmpty(path) ? "" : path.TrimStart('/');
                var currentPath = Path.Combine(rootPath, sanitizedPath);
                var filePath = Path.Combine(currentPath, file.FileName);

                if (System.IO.File.Exists(filePath))
                {
                    return Conflict(new { suggestedName = GetUniqueFileName(filePath) });
                }


                //using (var stream = new FileStream(filePath, FileMode.Create))
                //{
                //    file.CopyTo(stream);
                //}

                //// Save metadata to database
                //var fileMetadata = new FileMetadata
                //{
                //    FileName = file.FileName,
                //    FilePath = filePath,
                //    FileSize = file.Length,
                //    DateModified = DateTime.Now,
                //    ModifiedBy = "Momin",
                //    Owner = "Momin",
                //    ExpiryDate = expiryDate,
                //    IssuedBy = issuedBy
                //};

                //_context.FileMetadata.Add(fileMetadata);
                //await _context.SaveChangesAsync();

                //// Debug logging
                //Console.WriteLine($"Uploaded file: {filePath}");

                using (var transaction = await _context.Database.BeginTransactionAsync()) 
                {
                    try
                    {
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }

                        // Save metadata to database
                        var fileMetadata = new FileMetadata
                        {
                            FileName = file.FileName,
                            FilePath = filePath,
                            FileSize = file.Length,
                            DateModified = DateTime.Now,
                            ModifiedBy = "Momin", // Hardcoded for now
                            Owner = "Momin", // Hardcoded for now
                            ExpiryDate = expiryDate,
                            IssuedBy = issuedBy
                        };

                        _context.FileMetadata.Add(fileMetadata);
                        await _context.SaveChangesAsync();

                        await transaction.CommitAsync(); 
                    }
                    catch
                    {
                        await transaction.RollbackAsync(); 
                        if (System.IO.File.Exists(filePath))
                        {
                            System.IO.File.Delete(filePath); // Clean up the file if transaction fails
                        }
                        return StatusCode(500, "An error occurred while uploading the file.");
                    }
                }
            }

            return RedirectToAction("Index", new { path });
        }


        [HttpPost]
        public async Task<IActionResult> UploadFiles(List<IFormFile> files, string path)
        {
            if (files != null && files.Count > 0)
            {
                var sanitizedPath = string.IsNullOrEmpty(path) ? "" : path.TrimStart('/');
                var currentPath = Path.Combine(rootPath, sanitizedPath);

                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    var filePath = Path.Combine(currentPath, file.FileName);

                    if (System.IO.File.Exists(filePath))
                    {
                        return Conflict(new
                        {
                            fileName = file.FileName,
                            suggestedName = GetUniqueFileName(filePath),
                            index = i
                        });
                    }

                    var issuedByKey = $"issuedBy_{i}";
                    var expiryDateKey = $"expiryDate_{i}";

                    var issuedBy = Request.Form[issuedByKey];
                    var expiryDate = DateTime.TryParse(Request.Form[expiryDateKey], out DateTime expiry) ? (DateTime?)expiry : null;

                    using (var transaction = await _context.Database.BeginTransactionAsync())
                    {
                        try
                        {
                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                await file.CopyToAsync(stream);
                            }

                            // Save metadata to database
                            var fileMetadata = new FileMetadata
                            {
                                FileName = file.FileName,
                                FilePath = filePath,
                                FileSize = file.Length,
                                DateModified = DateTime.Now,
                                ModifiedBy = "Momin", // Hardcoded for now
                                Owner = "Momin", // Hardcoded for now
                                ExpiryDate = expiryDate,
                                IssuedBy = issuedBy
                            };

                            _context.FileMetadata.Add(fileMetadata);
                            await _context.SaveChangesAsync();

                            await transaction.CommitAsync();
                        }
                        catch
                        {
                            await transaction.RollbackAsync();
                            if (System.IO.File.Exists(filePath))
                            {
                                System.IO.File.Delete(filePath); // Clean up the file if transaction fails
                            }
                            return StatusCode(500, "An error occurred while uploading the file.");
                        }
                    }
                }
            }

            return RedirectToAction("Index", new { path });
        }

        

        [HttpPost]
        public IActionResult ReplaceFile(IFormFile file, string path)
        {
            if (file != null && file.Length > 0)
            {
                var sanitizedPath = string.IsNullOrEmpty(path) ? "" : path.TrimStart('/');
                var currentPath = Path.Combine(rootPath, sanitizedPath);
                var filePath = Path.Combine(currentPath, file.FileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }

                // Debug logging
                Console.WriteLine($"Uploaded file: {filePath}");
            }

            return RedirectToAction("Index", new { path });
        }

        private string GetUniqueDirectoryName(string path)
        {
            int count = 1;
            string uniquePath = path;
            while (Directory.Exists(uniquePath))
            {
                uniquePath = $"{path}({count})";
                count++;
            }
            return new DirectoryInfo(uniquePath).Name;
        }

        private string GetUniqueFileName(string path)
        {
            int count = 1;
            string uniquePath = path;
            string directory = Path.GetDirectoryName(path);
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);

            while (System.IO.File.Exists(uniquePath))
            {
                uniquePath = Path.Combine(directory, $"{fileNameWithoutExtension}({count}){extension}");
                count++;
            }
            return Path.GetFileName(uniquePath);
        }

        public IActionResult OpenFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return NotFound();
            }

            var decodedPath = System.Net.WebUtility.UrlDecode(path);
            var filePath = Path.Combine(rootPath, decodedPath.TrimStart('/'));

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound();
            }

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            var mimeType = "application/octet-stream";
            var fileExtension = Path.GetExtension(filePath).ToLowerInvariant();

            switch (fileExtension)
            {
                case ".jpg":
                case ".jpeg":
                    mimeType = "image/jpeg";
                    break;
                case ".png":
                    mimeType = "image/png";
                    break;
                case ".gif":
                    mimeType = "image/gif";
                    break;
                case ".pdf":
                    mimeType = "application/pdf";
                    break;
                case ".txt":
                    mimeType = "text/plain";
                    break;
                case ".html":
                    mimeType = "text/html";
                    break;
                    // Add other MIME types as necessary
            }

            return File(fileBytes, mimeType, Path.GetFileName(filePath));
        }

        [HttpDelete]
        public IActionResult DeleteItem(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return BadRequest("Invalid path");
            }

            var decodedPath = System.Net.WebUtility.UrlDecode(path);
            var fullPath = Path.Combine(rootPath, decodedPath.TrimStart('/'));

            try
            {
                if (Directory.Exists(fullPath))
                {
                    Directory.Delete(fullPath, true);
                }
                else if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
                else
                {
                    return NotFound();
                }

                return Ok();
            }
            catch
            {
                return StatusCode(500, "Error deleting item");
            }
        }

        [HttpPost]
        public IActionResult RenameItem(string path, string newName)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(newName))
            {
                return BadRequest("Invalid path or new name");
            }

            var decodedPath = System.Net.WebUtility.UrlDecode(path);
            var fullPath = Path.Combine(rootPath, decodedPath.TrimStart('/'));
            var newFullPath = Path.Combine(Path.GetDirectoryName(fullPath), newName);

            try
            {
                if (Directory.Exists(fullPath))
                {
                    Directory.Move(fullPath, newFullPath);
                }
                else if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Move(fullPath, newFullPath);
                }
                else
                {
                    return NotFound();
                }

                return Ok();
            }
            catch
            {
                return StatusCode(500, "Error renaming item");
            }
        }


        public async Task<IActionResult> GetItemInfo(string path, string type) 
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(type)) 
            {
                return BadRequest("Invalid path or type"); 
            }

            var decodedPath = System.Net.WebUtility.UrlDecode(path);
            var fullPath = Path.Combine(rootPath, decodedPath.TrimStart('/'));
            var relativePath = Path.GetRelativePath(rootPath, fullPath).Replace("\\", "/");

            try
            {
                bool isDirectory = type == "directory"; 
                bool isFile = type == "file"; 

                if (!isDirectory && !isFile) 
                {
                    return NotFound(); 
                }

                var fileMetadata = await _context.FileMetadata.FirstOrDefaultAsync(fm => fm.FilePath == fullPath);

                var createDate = isFile ? System.IO.File.GetCreationTime(fullPath).ToString("G") : Directory.GetCreationTime(fullPath).ToString("G"); 
                var modifiedDate = isFile ? System.IO.File.GetLastWriteTime(fullPath).ToString("G") : Directory.GetLastWriteTime(fullPath).ToString("G"); 
                var owner = "Unknown";
                var modifiedBy = "Unknown";
                var expiryDate = (DateTime?)null;
                var issuedBy = "Unknown";
                long size = 0;

                if (fileMetadata != null)
                {
                    owner = fileMetadata.Owner;
                    modifiedBy = fileMetadata.ModifiedBy;
                    expiryDate = fileMetadata.ExpiryDate;
                    issuedBy = fileMetadata.IssuedBy;
                    size = fileMetadata.FileSize;
                }
                else
                {
                    if (isFile) 
                    {
                        var fileInfo = new FileInfo(fullPath);
                        size = fileInfo.Length;
                    }
                    else if (isDirectory) 
                    {
                        var directoryInfo = new DirectoryInfo(fullPath);
                        size = directoryInfo.EnumerateFiles("*", SearchOption.AllDirectories).Sum(fi => fi.Length);
                    }
                }

                var info = new
                {
                    createDate,
                    createdBy = owner,
                    modifiedDate,
                    modifiedBy,
                    size = FormatSize(size),
                    location = relativePath,
                    expiryDate = expiryDate?.ToString("G") ?? "N/A",
                    issuedBy
                };

                return Json(info);
            }
            catch
            {
                return StatusCode(500, "Error retrieving item info");
            }
        }

        private string GetDirectorySize(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var dirInfo = new DirectoryInfo(path);
                    long size = dirInfo.GetFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
                    return FormatSize(size);
                }
                else if (System.IO.File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    return FormatSize(fileInfo.Length);
                }
                else
                {
                    return "Not Found";
                }
            }
            catch
            {
                return "Error";
            }
        }

        private string FormatSize(long bytes)
        {
            if (bytes >= 1073741824)
                return (bytes / 1073741824.0).ToString("0.00") + " GB";
            else if (bytes >= 1048576)
                return (bytes / 1048576.0).ToString("0.00") + " MB";
            else if (bytes >= 1024)
                return (bytes / 1024.0).ToString("0.00") + " KB";
            else
                return bytes + " B";
        }

        private List<Breadcrumb> GenerateBreadcrumbs(string path)
        {
            var breadcrumbs = new List<Breadcrumb>();
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            string cumulativePath = "";
            foreach (var part in parts)
            {
                cumulativePath = string.IsNullOrEmpty(cumulativePath) ? part : $"{cumulativePath}/{part}";
                breadcrumbs.Add(new Breadcrumb { Name = part, Path = cumulativePath });
            }

            return breadcrumbs;
        }

        // Other methods remain the same

        public class Breadcrumb
        {
            public string Name { get; set; }
            public string Path { get; set; }
        }


        [HttpPost]
        public IActionResult DownloadSelected([FromBody] List<string> selectedFiles)
        {
            if (selectedFiles == null || selectedFiles.Count == 0)
            {
                return BadRequest("No files selected.");
            }

            using (var memoryStream = new MemoryStream())
            {
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    foreach (var relativePath in selectedFiles)
                    {
                        var fullPath = Path.Combine(rootPath, relativePath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));

                        if (System.IO.File.Exists(fullPath))
                        {
                            var entryName = Path.GetFileName(fullPath);
                            archive.CreateEntryFromFile(fullPath, entryName);
                        }
                        else if (Directory.Exists(fullPath))
                        {
                            // Add directory and its contents to the zip file
                            AddDirectoryToZip(archive, fullPath, Path.GetFileName(fullPath));
                        }
                    }
                }

                memoryStream.Seek(0, SeekOrigin.Begin);
                return File(memoryStream.ToArray(), "application/zip", "selected_files.zip");
            }
        }

        // Helper method to add a directory and its contents to a zip file
        private void AddDirectoryToZip(ZipArchive archive, string sourceDir, string entryName)
        {
            var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relativePath = file.Substring(sourceDir.Length + 1);
                archive.CreateEntryFromFile(file, Path.Combine(entryName, relativePath).Replace("\\", "/"));
            }

            var directories = Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories);
            foreach (var directory in directories)
            {
                var relativePath = directory.Substring(sourceDir.Length + 1) + "/";
                archive.CreateEntry(Path.Combine(entryName, relativePath).Replace("\\", "/"));
            }

            // Handle the case for empty directories
            if (files.Length == 0 && directories.Length == 0)
            {
                archive.CreateEntry(Path.Combine(entryName, "empty_directory.txt").Replace("\\", "/"));
            }
        }

        [HttpPost]
        public IActionResult DeleteSelected([FromBody] List<string> paths)
        {
            try
            {
                foreach (var path in paths)
                {
                    var decodedPath = System.Net.WebUtility.UrlDecode(path);
                    var fullPath = Path.Combine(rootPath, decodedPath.TrimStart('/'));

                    if (Directory.Exists(fullPath))
                    {
                        Directory.Delete(fullPath, true);
                    }
                    else if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                    }
                }
                return Ok();
            }
            catch
            {
                return StatusCode(500, "Error deleting items");
            }
        }
    }
}
