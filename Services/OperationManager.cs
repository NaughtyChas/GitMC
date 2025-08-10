using System.Collections.ObjectModel;
using System.Linq;
using GitMC.Models;

namespace GitMC.Services;

public class OperationManager : IOperationManager
{
    private readonly ObservableCollection<OperationInfo> _ops = new();
    public ReadOnlyObservableCollection<OperationInfo> Operations { get; }

    public OperationManager()
    {
        Operations = new ReadOnlyObservableCollection<OperationInfo>(_ops);
    }

    public OperationInfo Start(string savePath, OperationType type, int totalSteps = 0, string message = "")
    {
        var op = new OperationInfo { SavePath = savePath, Type = type, TotalSteps = totalSteps, Message = message, };
        op.Status = OperationStatus.Running;
        _ops.Add(op);
        return op;
    }

    public void Update(OperationInfo op, int? current = null, int? total = null, string? message = null)
    {
        if (current.HasValue) op.CurrentStep = current.Value;
        if (total.HasValue) op.TotalSteps = total.Value;
        if (message != null) op.Message = message;
    }

    public void Complete(OperationInfo op, bool success, string? message = null)
    {
        if (message != null) op.Message = message;
        op.Status = success ? OperationStatus.Succeeded : OperationStatus.Failed;
        // Keep completed operations for a short time; do not remove immediately to allow UI to consume
    }

    public OperationInfo? GetActive(string savePath, OperationType? type = null)
    {
        return _ops.LastOrDefault(o => o.SavePath == savePath && o.Status == OperationStatus.Running && (type == null || o.Type == type));
    }
}
