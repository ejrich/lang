using Lang.Project;

namespace Lang.Backend
{
    public interface IBackend
    {
        void Build(ProjectFile project, ProgramGraph programGraph, BuildSettings buildSettings);
    }
}
