using System;
using TheArchive.Interfaces;

namespace ReTFO.Archipelago.FeaturesAPI;

/// <summary>
/// Tracks the log source of the currently processing Feature so that Processors 
///  (such as Expedition.Processor) can log errors to them.
/// </summary>
/// <seealso cref="StatefulFeatureLogger"/>
public static class FeatureLogger // : IArchiveLogger
{
    /// <summary>
    /// The current logger. Defaults to the plugin's logger if necessary
    /// </summary>
    public static IArchiveLogger Current
    {
        get => m_current ??= Plugin.Get().Logger;
        set => m_current = value;
    }
    private static IArchiveLogger? m_current = null;

    /// <summary>
    /// Logs a green success info message
    /// </summary>
    /// <param name="msg">The message to log</param>
    public static void Success(string msg) => Current.Success(msg);

    /// <summary>
    /// Logs a light-blue notice message
    /// </summary>
    /// <param name="msg">The message to log</param>
    public static void Notice(string msg) => Current.Notice(msg);

    /// <summary>
    /// Logs a custom colored message.
    /// </summary>
    /// <param name="col">Console Color to use.</param>
    /// <param name="msg">The mesasge to log</param>
    public static void Msg(ConsoleColor col, string msg) => Current.Msg(col, msg);

    /// <summary>
    /// Logs a white colored info message.
    /// </summary>
    /// <param name="msg">The message to log</param>
    public static void Info(string msg) => Current.Info(msg);

    /// <summary>
    /// Logs a red colored fail info message.
    /// </summary>
    /// <param name="msg">The message to log</param>
    public static void Fail(string msg) => Current.Fail(msg);

    /// <summary>
    /// Logs a gray debug message.
    /// </summary>
    /// <param name="msg">The message to log</param>
    public static void Debug(string msg) => Current.Debug(msg);

    /// <summary>
    /// Logs a yellow colored warning message.
    /// </summary>
    /// <param name="msg">The message to log</param>
    public static void Warning(string msg) => Current.Warning(msg);

    /// <summary>
    /// Logs a red colored error message.
    /// </summary>
    /// <param name="msg">The message to log</param>
    public static void Error(string msg) => Current.Error(msg);

    /// <summary>
    /// Logs the exception message and the stacktrace.
    /// </summary>
    /// <param name="msg">Thrown exception to log</param>
    public static void Exception(Exception ex) => Current.Exception(ex);
}
