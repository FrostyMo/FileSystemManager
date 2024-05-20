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

            if (!Directory.Exists(newPath))
            {
                Directory.CreateDirectory(newPath);
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
        public IActionResult UploadFile(IFormFile file, string path)
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
    }
}
