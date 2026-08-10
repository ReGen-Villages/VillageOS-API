namespace vos.Service.Intake;

public static class SubmissionSize
{
    /// <summary>A submission is a form's worth of answers and a drawn boundary — kilobytes. The cap is
    /// generous against that and small enough that a body cannot cost the service its memory before
    /// anything has looked at it.</summary>
    public const long MaximumBytes = 256 * 1024;
}
