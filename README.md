# File System Manager

File System Manager is a web-based application designed to manage files and directories with ease. It provides functionalities such as viewing, uploading, downloading, deleting, renaming, and previewing files and directories. The application also supports bulk operations like downloading and deleting multiple files at once.

## Features

- **File and Directory Management**: View, create, delete, and rename files and directories.
- **Search**: Search for files and directories within the current path or across all directories.
- **File Upload**: Upload new files to the desired directory.
- **File Preview**: Preview supported file types (images, PDFs, text files) directly within the application.
- **Bulk Operations**: Select multiple files for bulk download or deletion.
- **Pagination**: Navigate through directories with pagination support for large directories.
- **Breadcrumb Navigation**: Easily navigate through directory hierarchies using breadcrumbs.

## Technologies Used

- **ASP.NET Core MVC**: Backend framework for handling server-side logic.
- **Entity Framework Core**: ORM for database operations.
- **Bootstrap**: Frontend framework for responsive design.
- **JavaScript**: Client-side scripting for interactivity.
- **Font Awesome**: Icon library for UI components.

## Setup Instructions

### Prerequisites

- .NET Core SDK
- SQL Server or any other supported database
- Visual Studio or any other preferred IDE

### Installation

1. **Clone the Repository**
    ```sh
    git clone https://github.com/yourusername/filesystem-manager.git
    cd filesystem-manager
    ```

2. **Configure Database**
    - Update the connection string in `appsettings.json` to point to your database.
    - Run the following commands to apply migrations and create the database:
      ```sh
      dotnet ef migrations add InitialCreate
      dotnet ef database update
      ```

3. **Run the Application**
    ```sh
    dotnet run
    ```

4. **Access the Application**
    - Open your web browser and navigate to `http://localhost:PORT_ASSIGNED`.

## Usage

### Viewing Files and Directories

- **Breadcrumb Navigation**: Use the breadcrumb links at the top to navigate through directories.
- **Pagination**: Use the pagination controls at the bottom to navigate through large directories.

### File and Directory Operations

- **Create Directory**: Click on the "Create New Directory" button, enter the directory name, and submit.
- **Upload File**: Click on the "Upload New File" button, select a file, optionally fill out additional metadata, and submit.
- **Preview File**: Double-click on a file to preview it (supported file types only).
- **Rename Item**: Right-click on an item, select "Rename", enter the new name, and submit.
- **Delete Item**: Right-click on an item and select "Delete". Confirm the deletion in the popup.

### Bulk Operations

- **Select Multiple Items**: Hold the Shift key and click on items to select multiple files or directories.
- **Download Selected**: Click on the download icon in the action banner to download selected items as a ZIP file.
- **Delete Selected**: Click on the delete icon in the action banner to delete selected items.

## Contribution

Contributions are welcome! Please fork the repository and submit a pull request for any enhancements or bug fixes.

## License

This project is licensed under the MIT License.

## Contact

For any questions or feedback, please reach out to mominsalar3@gmail.com.

---

Thank you for using File System Manager! We hope it makes managing your files easier and more efficient.
