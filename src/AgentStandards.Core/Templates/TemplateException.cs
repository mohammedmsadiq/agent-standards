namespace AgentStandards.Core.Templates;

public sealed class TemplateException(string message, Exception? inner = null) : Exception(message, inner);
