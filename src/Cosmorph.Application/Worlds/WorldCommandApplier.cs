using System.Collections.Immutable;
using Cosmorph.Domain.Wardens;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Worlds;

/// <summary>
/// Applies validated commands at the start of a tick. Commands never bypass domain invariants and
/// are always scoped to a single world.
/// </summary>
public static class WorldCommandApplier
{
    public const int MaxWardensPerWorld = 8;

    public static WorldState Apply(WorldState state, WorldCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);

        if (command.WorldId != state.Id)
        {
            throw new InvalidOperationException("A command may never be applied to a different world.");
        }

        return command.Kind switch
        {
            WorldCommandKind.PauseWorld => state with { IsPaused = true, Version = state.Version + 1 },
            WorldCommandKind.ResumeWorld => state with { IsPaused = false, Version = state.Version + 1 },
            WorldCommandKind.SetWardenCharter => ApplyCharter(state, command.Charter),
            _ => state,
        };
    }

    private static WorldState ApplyCharter(WorldState state, WardenCharter? charter)
    {
        if (charter is null)
        {
            return state;
        }

        if (charter.Validate(state.Cells.Length) is not null)
        {
            return state;
        }

        if (!state.Content.TryGet(charter.ControlledSpecies, out _))
        {
            return state;
        }

        var wardens = state.Wardens.ToBuilder();
        var index = -1;
        for (var i = 0; i < wardens.Count; i++)
        {
            if (wardens[i].Id == charter.Id)
            {
                index = i;
                break;
            }
        }

        if (index >= 0)
        {
            wardens[index] = charter;
        }
        else
        {
            if (wardens.Count >= MaxWardensPerWorld)
            {
                return state;
            }

            wardens.Add(charter);
        }

        return state with
        {
            Wardens = [.. wardens.OrderBy(w => w.Id.Value, StringComparer.Ordinal)],
            Version = state.Version + 1,
        };
    }
}
