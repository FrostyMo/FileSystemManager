using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Data.Entity;

namespace FileSystemManager.Controllers
{
    public class FileSystemController : Controller
    {
        private readonly string rootPath;
        private readonly string _recycleBinPath;
        private readonly IConfiguration configuration;
        private readonly ApplicationDbContext _context;
        public FileSystemController(IConfiguration configuration, ApplicationDbContext context)
        {
            this.configuration = configuration;
            _context = context;
            rootPath = configuration["PhotoDirectory"] ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
            _recycleBinPath = configuration["RecycleBinDirectory"] ?? Path.Combine(Directory.GetCurrentDirectory(), "RecycleBin");
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
        public async Task<IActionResult> PermanentlyDeleteItem(int id)
        {
            try
            {
                var fileMetadata = await _context.FileMetadata.FindAsync(id);
                if (fileMetadata == null || !fileMetadata.IsDeleted)
                {
                    return NotFound();
                }
                if (System.IO.File.Exists(fileMetadata.DeletedPath))
                {
                    System.IO.File.Delete(fileMetadata.DeletedPath);
                }
                _context.FileMetadata.Remove(fileMetadata);
                await _context.SaveChangesAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error deleting item: " + ex.Message);
            }
        }
        [HttpDelete]
        public async Task<IActionResult> DeleteItem(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return BadRequest("Invalid path");
            }

            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                string fullPath = null;
                string recycleBinPath = null;
                bool isDirectory = false;

                try
                {
                    var decodedPath = WebUtility.UrlDecode(path).Replace('\\', '/');
                    fullPath = Path.Combine(rootPath, decodedPath).Replace('\\', '/');
                    recycleBinPath = Path.Combine(_recycleBinPath, Path.GetFileName(fullPath)).Replace('/', '\\');
                    Directory.CreateDirectory(Path.GetDirectoryName(recycleBinPath));

                    if (Directory.Exists(fullPath))
                    {
                        Directory.Move(fullPath, recycleBinPath);
                        isDirectory = true;
                    }
                    else if (System.IO.File.Exists(fullPath))
                    {
                        if (System.IO.File.Exists(recycleBinPath))
                        {
                            System.IO.File.Delete(recycleBinPath);
                        }
                        System.IO.File.Move(fullPath, recycleBinPath);
                    }
                    else
                    {
                        return NotFound();
                    }

                    var normalizedPath = decodedPath;
                    var fileMetadata = _context.FileMetadata.AsEnumerable()
                        .FirstOrDefault(fm => fm.FilePath.Replace('\\', '/') == normalizedPath);

                    if (fileMetadata != null)
                    {
                        fileMetadata.IsDeleted = true;
                        fileMetadata.DeletedPath = recycleBinPath;
                        fileMetadata.DateModified = DateTime.UtcNow;
                        _context.Update(fileMetadata);
                        await _context.SaveChangesAsync();
                    }

                    await transaction.CommitAsync();
                    return Ok();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();

                    // Roll back local file system changes
                    if (isDirectory && Directory.Exists(recycleBinPath))
                    {
                        Directory.Move(recycleBinPath, fullPath);
                    }
                    else if (System.IO.File.Exists(recycleBinPath))
                    {
                        System.IO.File.Move(recycleBinPath, fullPath);
                    }

                    return StatusCode(500, "Error deleting item: " + ex.Message);
                }
            }
        }

        [HttpGet("RecycleBin")]
        public IActionResult RecycleBin()
        {
            return View();
        }
        [HttpGet("FileSystem/RecycleBinItems")]
        public async Task<IActionResult> GetRecycleBinItems()
        {
            try
            {
                var recycleBinItems = _context.FileMetadata
                    .Where(fm => fm.IsDeleted)
                    .ToList();

                return Json(recycleBinItems);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error retrieving recycle bin items: " + ex.Message);
            }
        }
        [HttpPut("FileSystem/RestoreItem/{fileId}")]
        public async Task<IActionResult> RestoreItem(int fileId)
        {
            try
            {
                var fileMetadata = await _context.FileMetadata.FindAsync(fileId);
                if (fileMetadata == null)
                {
                    return NotFound("File not found in recycle bin.");
                }
                string deletedPath = fileMetadata.DeletedPath;
                string restorePath = DetermineRestorePath(fileMetadata.FilePath);
                if (System.IO.File.Exists(deletedPath))
                {
                    System.IO.File.Move(deletedPath, restorePath);
                    fileMetadata.IsDeleted = false;
                    fileMetadata.DeletedPath = null;
                    await _context.SaveChangesAsync();

                    return Ok(new { message = "File restored successfully." });
                }
                else
                {
                    return NotFound("File not found in recycle bin.");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error restoring file: " + ex.Message);
            }
        }
        private string DetermineRestorePath(string uploadedFilePath)
        {
            return uploadedFilePath;
        }
        [HttpPost]
        public IActionResult RenameItem(string path, string newName)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(newName))
            {
                return BadRequest("Invalid path or new name");
            }

            var decodedPath = System.Net.WebUtility.UrlDecode(path).TrimStart('/').Replace('\\', '/');
            var fullPath = Path.Combine(rootPath, decodedPath).Replace('\\', '/');
            var extension = Path.GetExtension(fullPath);

            if (!newName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                newName = Path.ChangeExtension(newName, extension);
            }

            var newFullPath = Path.Combine(Path.GetDirectoryName(fullPath), newName).Replace('\\', '/');

            using (var transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    bool itemExistsLocally = false;

                    if (Directory.Exists(fullPath))
                    {
                        Directory.Move(fullPath, newFullPath);
                        itemExistsLocally = true;
                    }
                    else if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Move(fullPath, newFullPath);
                        itemExistsLocally = true;
                    }

                    var normalizedPath = decodedPath.Replace('\\', '/');
                    var fileMetadataList = _context.FileMetadata.ToList();
                    var fileMetadata = fileMetadataList.FirstOrDefault(fm => fm.FilePath.Replace('\\', '/') == normalizedPath);

                    if (fileMetadata == null)
                    {
                        return NotFound("File metadata not found in the database");
                    }

                    if (itemExistsLocally)
                    {
                        // Update file metadata with new name and path
                        var newFilePath = Path.Combine(Path.GetDirectoryName(normalizedPath), newName).Replace('\\', '/');
                        fileMetadata.FileName = newName;
                        fileMetadata.FilePath = newFilePath;
                        fileMetadata.DateModified = DateTime.Now;
                        _context.FileMetadata.Update(fileMetadata);
                        _context.SaveChanges();
                        transaction.Commit();
                    }
                    else
                    {
                        // Mark the item as not found or deleted in the database
                        fileMetadata.IsDeleted = true;
                        fileMetadata.DeletedPath = null;
                        _context.FileMetadata.Update(fileMetadata);
                        _context.SaveChanges();
                        transaction.Commit();

                        return NotFound("Item not found on the local file system. Metadata marked as deleted.");
                    }

                    return Ok();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    return StatusCode(500, "Error renaming item: " + ex.Message);
                }
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
        public class Breadcrumb
        {
            public string Name { get; set; }
            public string Path { get; set; }
        }
    }
}
