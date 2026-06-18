using Microsoft.Extensions.AI;

public interface ICommonTool
{
    AIFunction Function { get; }

    string Prompt { get; }
}
