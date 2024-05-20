using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Linq;

namespace FileSystemManager.Controllers
{
    public class FileSystemController : Controller
    {
        private readonly string rootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");

        public IActionResult Index(string path = "")
        {
            var sanitizedPath = string.IsNullOrEmpty(path) ? "" : path.TrimStart('/');
            var currentPath = Path.Combine(rootPath, sanitizedPath);

            if (!Directory.Exists(currentPath))
            {
                return NotFound();
            }

            var directories = Directory.GetDirectories(currentPath).Select(d => new DirectoryInfo(d)).ToList();
            var files = Directory.GetFiles(currentPath).Select(f => new FileInfo(f)).ToList();

            ViewBag.CurrentPath = sanitizedPath;
            ViewBag.Directories = directories;
            ViewBag.Files = files;
            ViewBag.ParentPath = string.IsNullOrEmpty(sanitizedPath) ? "" : Path.GetDirectoryName(sanitizedPath.Replace("/", "\\"))?.Replace("\\", "/") ?? "";

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
        public IActionResult UploadFile(IFormFile file, string path)
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

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }

                // Debug logging
                Console.WriteLine($"Uploaded file: {filePath}");
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

            // Decode the URL-encoded path
            var decodedPath = System.Net.WebUtility.UrlDecode(path);
            var filePath = Path.Combine(rootPath, decodedPath.TrimStart('/'));

            // Debug logging
            Console.WriteLine($"Opening file: {filePath}");

            if (!System.IO.File.Exists(filePath))
            {
                return NotFound();
            }

            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            var mimeType = "application/octet-stream";
            var fileExtension = Path.GetExtension(filePath).ToLowerInvariant();

            // Determine MIME type based on file extension
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

        [HttpGet]
        public IActionResult GetItemInfo(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return BadRequest("Invalid path");
            }

            var decodedPath = System.Net.WebUtility.UrlDecode(path);
            var fullPath = Path.Combine(rootPath, decodedPath.TrimStart('/'));
            var relativePath = Path.GetRelativePath(rootPath, fullPath).Replace("\\", "/");

            try
            {
                var info = new
                {
                    createDate = System.IO.File.GetCreationTime(fullPath).ToString("G"),
                    createdBy = "Unknown", // Placeholder, should be replaced with actual data
                    modifiedDate = System.IO.File.GetLastWriteTime(fullPath).ToString("G"),
                    modifiedBy = "Unknown", // Placeholder, should be replaced with actual data
                    size = GetDirectorySize(fullPath),
                    location = relativePath
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
    }
}
