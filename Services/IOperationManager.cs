using System;
using System.Collections.ObjectModel;
using GitMC.Models;

namespace GitMC.Services;

public interface IOperationManager
{
    ReadOnlyObservableCollection<OperationInfo> Operations { get; }
    OperationInfo Start(string savePath, OperationType type, int totalSteps = 0, string message = "");
    void Update(OperationInfo op, int? current = null, int? total = null, string? message = null);
    void Complete(OperationInfo op, bool success, string? message = null);
    OperationInfo? GetActive(string savePath, OperationType? type = null);
}
