using Microsoft.AspNetCore.Mvc;
using System.IO;

namespace FileSystemManager.Controllers
{
    public class FileSystemController : Controller
    {
        private readonly string rootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");

        public IActionResult Index()
        {
            var directories = Directory.GetDirectories(rootPath).Select(d => new DirectoryInfo(d).Name).ToList();
            var files = Directory.GetFiles(rootPath).Select(f => Path.GetFileName(f)).ToList();

            ViewBag.Directories = directories;
            ViewBag.Files = files;

            return View();
        }

        [HttpGet]
        public IActionResult CreateDirectory()
        {
            return View();
        }

        [HttpPost]
        public IActionResult CreateDirectory(string directoryName)
        {
            var path = Path.Combine(rootPath, directoryName);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return RedirectToAction("Index");
        }

        [HttpGet]
        public IActionResult UploadFile()
        {
            return View();
        }

        [HttpPost]
        public IActionResult UploadFile(IFormFile file)
        {
            if (file != null && file.Length > 0)
            {
                var filePath = Path.Combine(rootPath, file.FileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    file.CopyTo(stream);
                }
            }

            return RedirectToAction("Index");
        }
    }
}