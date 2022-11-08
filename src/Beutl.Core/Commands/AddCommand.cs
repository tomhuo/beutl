﻿using System.Collections;

namespace Beutl.Commands;

internal sealed class AddCommand : IRecordableCommand
{
    public AddCommand(IList list, object? item, int index)
    {
        List = list;
        Item = item;
        Index = index;
    }

    public IList List { get; }

    public object? Item { get; }

    public int Index { get; }

    public void Do()
    {
        List.Insert(Index, Item);
    }

    public void Redo()
    {
        Do();
    }

    public void Undo()
    {
        List.Remove(Item);
    }
}
