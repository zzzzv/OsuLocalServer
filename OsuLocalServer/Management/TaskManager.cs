using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace OsuLocalServer.Management;

public class TaskLogger
{
    private readonly Action<string, string> _write;

    internal TaskLogger(Action<string, string> write) => _write = write;

    public void Info(string message) => _write("INFO", message);
    public void Warn(string message) => _write("WARN", message);
    public void Error(string message) => _write("ERROR", message);
}

public delegate Task TaskHandler(IServiceProvider services, TaskLogger log, CancellationToken ct);

public enum TaskState { Idle, Running, Completed, Cancelled, Faulted }

public class TaskManager
{
    private CancellationTokenSource? _cts;
    private TaskState _state;
    private readonly ConcurrentQueue<LogEntry> _logBuffer = new();
    private readonly IHubContext<ManagementHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;

    public record LogEntry(string Time, string Level, string Message);

    public TaskManager(IHubContext<ManagementHub> hub, IServiceScopeFactory scopeFactory)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
    }

    public TaskState State => _state;

    public bool Start(TaskHandler task)
    {
        if (_state != TaskState.Idle) return false;

        _state = TaskState.Running;
        _cts = new CancellationTokenSource();
        var logger = new TaskLogger(WriteLog);
        var token = _cts.Token;

        if (_logBuffer.Count > 0)
            WriteLog("INFO", "━━━━━━━━━━━━━━━━━━ 新任务 ━━━━━━━━━━━━━━━━━━");
        logger.Info("任务已创建");

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            try
            {
                await task(scope.ServiceProvider, logger, token);
                _state = token.IsCancellationRequested ? TaskState.Cancelled : TaskState.Completed;
                logger.Info(_state == TaskState.Completed ? "任务已完成" : "任务已取消");
            }
            catch (OperationCanceledException)
            {
                _state = TaskState.Cancelled;
                logger.Warn("任务已取消");
            }
            catch (Exception ex)
            {
                _state = TaskState.Faulted;
                logger.Error(ex.ToString());
            }
            finally
            {
                _cts = null;
                _ = _hub.Clients.All.SendAsync("TaskDone", _state.ToString());
                _state = TaskState.Idle;
            }
        });

        return true;
    }

    public void Cancel()
    {
        if (_state != TaskState.Running) return;
        _cts?.Cancel();
        var logger = new TaskLogger(WriteLog);
        logger.Warn("正在取消...");
    }

    public int LogCount => _logBuffer.Count;

    public string[] GetAllLogs() =>
        _logBuffer.Select(e => $"[{e.Time}] [{e.Level}] {e.Message}").ToArray();

    public LogEntry[] GetLogEntries() => _logBuffer.ToArray();

    private void WriteLog(string level, string message)
    {
        var entry = new LogEntry(DateTime.Now.ToString("HH:mm:ss"), level, message);
        _logBuffer.Enqueue(entry);
        _ = _hub.Clients.All.SendAsync("Log", entry.Time, entry.Level, entry.Message);
    }
}
