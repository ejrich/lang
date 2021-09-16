using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Lang.Project
{
    public interface IProjectInterpreter
    {
        Project LoadProject(string projectPath);
    }

    public class ProjectInterpreter : IProjectInterpreter
    {
        private const string ProjectFileExtension = ".olproj";
        private const string SourceFileExtension = ".ol";

        public Project LoadProject(string projectPath)
        {
            // 1. Check if project file is null
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                projectPath = GetProjectPathInDirectory(Directory.GetCurrentDirectory());
            }

            // 2. Load the project file
            var fileAttributes = File.GetAttributes(projectPath);

            if (fileAttributes.HasFlag(FileAttributes.Directory))
                projectPath = GetProjectPathInDirectory(projectPath);

            var projectFileContents = File.ReadAllText(projectPath);

            var projectFile = JsonSerializer.Deserialize<ProjectFile>(projectFileContents,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            // 3. Recurse through the directories and load the files to build
            var sourceFiles = GetSourceFiles(new DirectoryInfo(Path.GetDirectoryName(projectPath)));

            return new Project
            {
                Name = projectFile.Name,
                BuildFiles = sourceFiles.ToList()
            };
        }

        private static string GetProjectPathInDirectory(string directory)
        {
            // a. Search for an olproj file in the current directory
            var projectPath = Directory.EnumerateFiles(Directory.GetCurrentDirectory())
                .FirstOrDefault(_ => _.EndsWith(ProjectFileExtension));

            // b. If no project file, throw and exit
            if (projectPath == null)
                throw new ArgumentException("Project file not found in current directory");

            return projectPath;
        }

        private static IEnumerable<string> GetSourceFiles(DirectoryInfo directory)
        {
            foreach (var sourceFile in directory.GetFiles().Where(_ => _.Name.EndsWith(SourceFileExtension)))
            {
                yield return sourceFile.FullName;
            }

            foreach (var subDirectory in directory.GetDirectories().Where(_ => _.Name != "bin"))
            {
                foreach (var sourceFile in GetSourceFiles(subDirectory))
                {
                    yield return sourceFile;
                }
            }
        }
    }
}
