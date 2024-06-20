using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : DbContext
{
    public DbSet<FileMetadata> FileMetadata { get; set; }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
}

public class FileMetadata
{
    public int Id { get; set; }
    public string FileName { get; set; }
    public string FilePath { get; set; }
    public long FileSize { get; set; }
    public DateTime DateModified { get; set; }
    public string ModifiedBy { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string IssuedBy { get; set; }
    public string Owner { get; set; }
    public bool IsDeleted { get; set; }
    public string? DeletedPath { get; set; }
}