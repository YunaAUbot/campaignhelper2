// <copyright file="PendingTaskDrain.cs" company="None">Copyright (c) None.</copyright>
namespace CampaignHelper;

public static class PendingTaskDrain
{
    public static bool DrainAndClear<T>(ref Task<T>? pending, TimeSpan timeout)
    {
        var task = pending;
        if (task is null)
        {
            return true;
        }

        try
        {
            if (!task.Wait(timeout))
            {
                return false;
            }
        }
        catch (AggregateException)
        {
            // Cancellation or a captured terminal failure still means no work remains.
        }

        pending = null;
        return true;
    }
}
