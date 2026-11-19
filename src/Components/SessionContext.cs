using Donkeh.UILab;
using VectorDotNet.Core;

namespace VectorDotNet.Components;

/// <summary>Shares the editor session with every panel.</summary>
internal static class SessionContext
{
    public static readonly Context<EditorSession> Value = new(new EditorSession());
}
