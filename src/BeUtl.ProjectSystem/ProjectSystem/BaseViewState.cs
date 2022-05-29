﻿using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace BeUtl.ProjectSystem;

public abstract class BaseViewState : INotifyPropertyChanged, IJsonSerializable
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetAndRaise<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

            return true;
        }
        else
        {
            return false;
        }
    }

    public abstract void ReadFromJson(JsonNode json);

    public abstract void WriteToJson(ref JsonNode json);
}
