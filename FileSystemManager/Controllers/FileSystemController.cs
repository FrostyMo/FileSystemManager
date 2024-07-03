using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Immutable;
using System.Net;
using System.IO;
using System.IO.Compression;

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
        public async Task<IActionResult> CreateDirectory(string directoryName, string path)
        {
            var sanitizedPath = string.IsNullOrEmpty(path) ? "" : path.TrimStart('/');
            var currentPath = Path.Combine(rootPath, sanitizedPath);
            var newPath = Path.Combine(currentPath, directoryName);

            if (Directory.Exists(newPath))
            {
                return Conflict(new { suggestedName = GetUniqueDirectoryName(newPath) });
            }

            Directory.CreateDirectory(newPath);

            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {

                    // Save metadata to database
                    var fileMetadata = new FileMetadata
                    {
                        FileName = directoryName,
                        FilePath = newPath,
                        DateModified = DateTime.Now,
                        ModifiedBy = "Momin", // Hardcoded for now
                        Owner = "Momin", // Hardcoded for now
                        IsFolder = true,
                        IssuedBy = "N/A",
                    };

                    _context.FileMetadata.Add(fileMetadata);
                    await _context.SaveChangesAsync();

                    await transaction.CommitAsync();
                }
                catch (Exception exc)
                {
                    await transaction.RollbackAsync();
                    if (Directory.Exists(newPath))
                    {
                        Directory.Delete(newPath, true); // Clean up the file if transaction fails
                    }
                    return StatusCode(500, "An error occurred while uploading the file." + exc.Message);
                }
            }

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
                        catch (Exception exc)
                        {
                            await transaction.RollbackAsync();
                            if (System.IO.File.Exists(filePath))
                            {
                                System.IO.File.Delete(filePath); // Clean up the file if transaction fails
                            }
                            return StatusCode(500, "An error occurred while uploading the file." + exc.Message);
                        }
                    }
                }
            }

            return RedirectToAction("Index", new { path });
        }

        [HttpPost]
        public async Task<IActionResult> ResolveConflict(List<IFormFile> files, string path, string action)
        {
            if (files != null && files.Count > 0)
            {
                var sanitizedPath = string.IsNullOrEmpty(path) ? "" : path.TrimStart('/');
                var currentPath = Path.Combine(rootPath, sanitizedPath);

                for (int i = 0; i < files.Count; i++)
                {
                    var file = files[i];
                    var filePath = Path.Combine(currentPath, file.FileName);

                    if (action != "replace" && System.IO.File.Exists(filePath))
                    {
                        return Conflict(new
                        {
                            fileName = file.FileName,
                            suggestedName = GetUniqueFileName(filePath),
                            index = i
                        });
                    }

                    if (action == "replace" && System.IO.File.Exists(filePath))
                    {
                        System.IO.File.Delete(filePath);
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
                                IssuedBy = issuedBy,
                                IsFolder = false
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

        [HttpDelete("FileSystem/PermanentlyDeleteItem/{id}")]
        public async Task<IActionResult> PermanentlyDeleteItem(int id)
        {
            FileMetadata fileMetadata = null;
            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    fileMetadata = await _context.FileMetadata.FindAsync(id);
                    if (fileMetadata == null || !fileMetadata.IsDeleted)
                    {
                        return NotFound("File metadata not found or the item is not marked as deleted.");
                    }
                    string deletedPath = fileMetadata.DeletedPath;
                    bool fileExists = System.IO.File.Exists(deletedPath);
                    bool directoryExists = Directory.Exists(deletedPath);

                    if (!fileExists && !directoryExists)
                    {
                        return NotFound("File or directory not found in recycle bin.");
                    }
                    // Remove the file or directory from the file system
                    if (fileExists)
                    {
                        System.IO.File.Delete(deletedPath);
                    }
                    else if (directoryExists)
                    {
                        Directory.Delete(deletedPath, true);
                    }
                    _context.FileMetadata.Remove(fileMetadata);
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return Ok("Item permanently deleted.");
                }
                catch (Exception ex)
                {

                    await transaction.RollbackAsync();
                    try
                    {
                        if (fileMetadata != null)
                        {
                            string deletedPath = fileMetadata.DeletedPath;
                            bool restoreFileExists = System.IO.File.Exists(deletedPath);
                            bool restoreDirectoryExists = Directory.Exists(deletedPath);

                            // If the item was deleted, recreate the file or directory
                            if (!restoreFileExists && !restoreDirectoryExists)
                            {
                                if (System.IO.File.Exists(fileMetadata.FilePath))
                                {
                                    System.IO.File.Move(fileMetadata.FilePath, deletedPath);
                                }
                                else if (Directory.Exists(fileMetadata.FilePath))
                                {
                                    Directory.Move(fileMetadata.FilePath, deletedPath);
                                }
                            }
                        }
                    }
                    catch (Exception rollbackEx)
                    {
                        // Log the rollback exception if needed
                        return StatusCode(500, $"Error deleting item: {ex.Message}, rollback error: {rollbackEx.Message}");
                    }

                    return StatusCode(500, "Error deleting item: " + ex.Message);
                }
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteItem(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return BadRequest("Invalid path");
            }

            string recycleBinPath = null;
            string fullPath = null;
            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    var decodedPath = WebUtility.UrlDecode(path).Replace('\\', '/');
                    //var sanitizedPath = string.IsNullOrEmpty(decodedPath) ? "" : decodedPath.TrimStart('/');
                    fullPath = Path.Combine(rootPath, decodedPath.TrimStart('/').Replace("/", Path.DirectorySeparatorChar.ToString()));
                    //fullPath = Path.GetFullPath(fullPath).Replace('\\', '/');
                    recycleBinPath = Path.Combine(_recycleBinPath, Path.GetFileName(fullPath)).Replace('\\', '/');
                    Directory.CreateDirectory(Path.GetDirectoryName(recycleBinPath));

                    if (Directory.Exists(fullPath))
                    {
                        if (Directory.Exists(recycleBinPath))
                        {
                            Directory.Delete(recycleBinPath, true);
                        }
                        Directory.Move(fullPath, recycleBinPath);
                        //var normalizedPath = decodedPath.Replace('\\', '/').TrimEnd('/');
                        UpdateMetadataForDirectory(fullPath, recycleBinPath);
                    }
                    else if (System.IO.File.Exists(fullPath))
                    {
                        if (System.IO.File.Exists(recycleBinPath))
                        {
                            System.IO.File.Delete(recycleBinPath);
                        }
                        System.IO.File.Move(fullPath, recycleBinPath);
                        //var normalizedPath = Path.Combine(rootPath, decodedPath.Replace('\\', '/').TrimEnd('/'));
                        var fileMetadata = _context.FileMetadata
                            .AsEnumerable()
                            .FirstOrDefault(fm => fm.FilePath.Replace('\\', '/') == fullPath);

                        if (fileMetadata != null)
                        {
                            fileMetadata.IsDeleted = true;
                            fileMetadata.DeletedPath = recycleBinPath.Replace('\\', '/');
                            fileMetadata.DateModified = DateTime.UtcNow;
                            fileMetadata.IsFolder = false;
                            _context.Update(fileMetadata);
                        }
                    }
                    else
                    {
                        return NotFound("File or directory not found");
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Ok();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    // Rollback local file system changes
                    if (!string.IsNullOrEmpty(recycleBinPath))
                    {
                        if (Directory.Exists(recycleBinPath))
                        {
                            if (!string.IsNullOrEmpty(fullPath) && Directory.Exists(recycleBinPath))
                            {
                                Directory.Move(recycleBinPath, fullPath);
                            }
                        }
                        else if (System.IO.File.Exists(recycleBinPath))
                        {
                            if (!string.IsNullOrEmpty(fullPath))
                            {
                                System.IO.File.Move(recycleBinPath, fullPath);
                            }
                        }
                    }
                    return StatusCode(500, "Error deleting item: " + ex.Message);
                }
            }
        }
        private void UpdateMetadataForDirectory(string normalizedPath, string recycleBinPath)
        {
            var fileMetadataList = _context.FileMetadata
                .Where(fm => fm.FilePath.StartsWith(normalizedPath))
                .ToList();

            foreach (var fileMetadata in fileMetadataList)
            {
                var originalFilePath = fileMetadata.FilePath;
                var relativePath = fileMetadata.FilePath.Replace(normalizedPath, "").TrimStart('/');
                fileMetadata.IsDeleted = true;
                fileMetadata.IsFolder = true; Directory.Exists(originalFilePath);
                recycleBinPath = recycleBinPath.Replace('/', '\\');
                relativePath = relativePath.Replace('\\', '/');
                if (relativePath.StartsWith("/") || relativePath.StartsWith("\\"))
                {
                    fileMetadata.DeletedPath = recycleBinPath + relativePath;
                }
                else
                {
                    fileMetadata.DeletedPath = Path.Combine(recycleBinPath, relativePath);
                }
                //it's working for the mulplte folder 
                //fileMetadata.DeletedPath = Path.Combine(recycleBinPath, relativePath.Replace('\\', '/'));
                //it's wokring for the singel folder 
                //fileMetadata.DeletedPath = recycleBinPath + relativePath.Replace('\\', '/');
                fileMetadata.DateModified = DateTime.UtcNow;
                _context.Update(fileMetadata);
                fileMetadata.FilePath = originalFilePath;

                if (Directory.Exists(originalFilePath))
                {
                    UpdateMetadataForDirectory(originalFilePath.Replace('\\', '/'), recycleBinPath);
                }
            }
        }
        [HttpGet("RecycleBin")]
        public IActionResult RecycleBin()
        {
            return View();
        }
        [HttpGet("GetRecycleBinItems")]
        public IActionResult GetRecycleBinItems()
        {
            try
            {
                // Query the database to get deleted items
                var deletedItems = _context.FileMetadata
                    .Where(fm => fm.IsDeleted)
                    .Select(fm => new
                    {
                        Id = fm.Id,
                        Name = fm.FileName,
                        Path = fm.FilePath,
                        DateModified = fm.DateModified,
                        DeletedPath = fm.DeletedPath,
                        IsDeleted = true,
                        IsFolder = fm.IsFolder,
                    })
                    .ToList();

                // Filter and map items to include both directories and files
                var recycleBinItems = deletedItems
                    .Select(item => new
                    {
                        item.Id,
                        item.Name,
                        Path = item.DeletedPath,
                        item.DateModified,
                        IsDirectory = Directory.Exists(item.DeletedPath) && !System.IO.File.Exists(item.DeletedPath),
                        item.IsDeleted,
                        item.IsFolder,
                    })
                    .ToList();

                return Json(recycleBinItems);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Error retrieving recycle bin items: " + ex.Message);
            }
        }


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
        private string DetermineRestorePath(string uploadedFilePath)
        {
            return uploadedFilePath;
        }
        [HttpPut("FileSystem/RestoreItem/{fileId}")]
        public async Task<IActionResult> RestoreItem(int fileId)
        {
            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    var fileMetadata = await _context.FileMetadata.FindAsync(fileId);
                    if (fileMetadata == null)
                    {
                        return NotFound("Item not found in recycle bin.");
                    }

                    string deletedPath = fileMetadata.DeletedPath;
                    string restorePath = DetermineRestorePath(fileMetadata.FilePath);

                    if (Directory.Exists(deletedPath))
                    {
                        string restorePathParent = Path.GetDirectoryName(restorePath);
                        if (!Directory.Exists(restorePathParent))
                        {
                            Directory.CreateDirectory(restorePathParent);
                        }

                        Directory.Move(deletedPath, restorePath);

                        fileMetadata.IsDeleted = false;
                        fileMetadata.DeletedPath = null;
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();
                        return Ok(new { message = "Folder restored successfully." });
                    }
                    else if (System.IO.File.Exists(deletedPath))
                    {
                        string restorePathParent = Path.GetDirectoryName(restorePath);
                        if (!Directory.Exists(restorePathParent))
                        {
                            Directory.CreateDirectory(restorePathParent);
                        }

                        System.IO.File.Move(deletedPath, restorePath);

                        fileMetadata.IsDeleted = false;
                        fileMetadata.DeletedPath = null;
                        await _context.SaveChangesAsync();
                        await transaction.CommitAsync();
                        return Ok(new { message = "File restored successfully." });
                    }
                    else
                    {
                        return NotFound("Item not found in recycle bin.");
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();

                    // Rollback local file system changes
                    try
                    {
                        // Ensure 'deletedPath' is initialized correctly here
                        var fileMetadata = await _context.FileMetadata.FindAsync(fileId);
                        if (fileMetadata != null)
                        {
                            string deletedPath = fileMetadata.DeletedPath;
                            string restorePath = DetermineRestorePath(fileMetadata.FilePath);

                            if (!string.IsNullOrEmpty(deletedPath))
                            {
                                if (Directory.Exists(restorePath))
                                {
                                    Directory.Move(restorePath, deletedPath);
                                }
                                else if (System.IO.File.Exists(restorePath))
                                {
                                    System.IO.File.Move(restorePath, deletedPath);
                                }
                            }
                        }
                    }
                    catch (Exception rollbackEx)
                    {
                        // Handle rollback exception if needed
                        return StatusCode(500, $"Error restoring item: {ex.Message}, rollback error: {rollbackEx.Message}");
                    }

                    return StatusCode(500, "Error restoring item: " + ex.Message);
                }
            }
        }
        private void MoveDirectoryContents(string sourceDirName, string destDirName)
        {
            // Ensure source directory exists
            if (!Directory.Exists(sourceDirName))
            {
                throw new DirectoryNotFoundException($"Source directory '{sourceDirName}' not found.");
            }

            // Ensure destination directory exists
            if (!Directory.Exists(destDirName))
            {
                throw new DirectoryNotFoundException($"Destination directory '{destDirName}' not found.");
            }

            // Get the files in the source directory and move them to the destination directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.MoveTo(temppath);
            }

            // Get the subdirectories in the source directory and move them to the destination directory.
            DirectoryInfo[] dirs = dir.GetDirectories();
            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(destDirName, subdir.Name);
                Directory.Move(subdir.FullName, temppath);
            }

            // Delete the source directory after moving its contents
            Directory.Delete(sourceDirName, true);
        }
        [HttpPost]
        public async Task<IActionResult> RenameItem(string path, string newName)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(newName))
            {
                return BadRequest("Invalid path or new name");
            }

            var decodedPath = System.Net.WebUtility.UrlDecode(path).TrimStart('/').Replace('\\', '/');
            var fullPath = Path.Combine(rootPath, decodedPath).Replace('\\', '/');
            var extension = Path.GetExtension(fullPath);

            bool isDirectory = Directory.Exists(fullPath);
            bool isFile = System.IO.File.Exists(fullPath);
            bool itemExistsLocally = false;

            if (!isDirectory && !isFile)
            {
                return NotFound("Item not found on the local file system.");
            }

            if (!isDirectory && !newName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                newName = Path.ChangeExtension(newName, extension);
            }

            var newFullPath = isDirectory
                ? Path.Combine(Path.GetDirectoryName(fullPath), newName).Replace('\\', '/')
                : Path.Combine(Path.GetDirectoryName(fullPath), newName).Replace('\\', '/');

            using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                try
                {
                    if (isDirectory)
                    {
                        Directory.Move(fullPath, newFullPath);
                        itemExistsLocally = true;
                        var normalizedPath = decodedPath.Replace('\\', '/');
                        var normalizedDirectoryPath = fullPath.Replace('\\', '/');
                        var affectedMetadata = _context.FileMetadata.AsEnumerable()
                            .Where(fm => fm.FilePath.StartsWith(normalizedDirectoryPath))
                            .ToList();

                        foreach (var fileMetadata in affectedMetadata)
                        {
                            var relativePath = fileMetadata.FilePath.Substring(normalizedDirectoryPath.Length);
                            var newFilePath = newFullPath + relativePath.Replace('\\', '/');
                            fileMetadata.FilePath = newFilePath;
                            fileMetadata.DateModified = DateTime.Now;
                            _context.FileMetadata.Update(fileMetadata);
                        }
                    }
                    else if (isFile)
                    {
                        System.IO.File.Move(fullPath, newFullPath);
                        itemExistsLocally = true;
                        var normalizedPath = decodedPath.Replace('\\', '/');
                        var fileMetadata = _context.FileMetadata.AsEnumerable()
                            .FirstOrDefault(fm => fm.FilePath.Replace('\\', '/') == normalizedPath);
                        if (fileMetadata == null)
                        {
                            System.IO.File.Move(newFullPath, fullPath);
                            return NotFound("File metadata not found in the database");
                        }
                        var directoryName = Path.GetDirectoryName(newFullPath);
                        var newFilePath = Path.Combine(Path.GetDirectoryName(normalizedPath), newName).Replace('\\', '/');
                        fileMetadata.FilePath = newFilePath;
                        fileMetadata.FileName = newName;
                        fileMetadata.DateModified = DateTime.Now;
                        _context.FileMetadata.Update(fileMetadata);
                    }
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return Ok(new { message = "File or directory renamed successfully." });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    // Rollback local changes if they were successfully applied
                    if (itemExistsLocally)
                    {
                        try
                        {
                            // Rollback local file system changes
                            if (isDirectory && Directory.Exists(newFullPath))
                            {
                                Directory.Move(newFullPath, fullPath);
                            }
                            else if (isFile && System.IO.File.Exists(newFullPath))
                            {
                                System.IO.File.Move(newFullPath, fullPath);
                            }
                        }
                        catch (Exception rollbackEx)
                        {
                            return StatusCode(500, "Error rolling back local changes: " + rollbackEx.Message);
                        }
                    }
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
