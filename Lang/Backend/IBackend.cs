namespace Lang.Backend
{
    public interface IBackend
    {
        void Build(ProgramGraph programGraph, Project.Project project, BuildSettings buildSettings);
    }
}
