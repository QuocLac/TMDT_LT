using System;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed record OrderTransitionResult(
    bool Applied,
    bool AlreadyApplied,
    string PreviousStatus,
    string CurrentStatus);

public interface IOrderStateService
{
    OrderTransitionResult Transition(
        Orders order,
        string targetStatus,
        string note,
        DateTime? occurredAt = null);
}
