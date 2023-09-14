﻿using System.ComponentModel;

namespace Beutl.Configuration;

public sealed class TelemetryConfig : ConfigurationBase
{
    public static readonly CoreProperty<bool?> Beutl_ApplicationProperty;
    public static readonly CoreProperty<bool?> Beutl_ViewTrackingProperty;
    public static readonly CoreProperty<bool?> Beutl_PackageManagementProperty;
    public static readonly CoreProperty<bool?> Beutl_Api_ClientProperty;
    public static readonly CoreProperty<bool?> Beutl_All_ErrorsProperty;

    static TelemetryConfig()
    {
        Beutl_ApplicationProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(Beutl_Application))
            .DefaultValue(null)
            .Register();

        Beutl_ViewTrackingProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(Beutl_ViewTracking))
            .DefaultValue(null)
            .Register();

        Beutl_PackageManagementProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(Beutl_PackageManagement))
            .DefaultValue(null)
            .Register();

        Beutl_Api_ClientProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(Beutl_Api_Client))
            .DefaultValue(null)
            .Register();

        Beutl_All_ErrorsProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(Beutl_All_Errors))
            .DefaultValue(null)
            .Register();
    }

    public bool? Beutl_Application
    {
        get => GetValue(Beutl_ApplicationProperty);
        set => SetValue(Beutl_ApplicationProperty, value);
    }

    public bool? Beutl_ViewTracking
    {
        get => GetValue(Beutl_ViewTrackingProperty);
        set => SetValue(Beutl_ViewTrackingProperty, value);
    }

    public bool? Beutl_PackageManagement
    {
        get => GetValue(Beutl_PackageManagementProperty);
        set => SetValue(Beutl_PackageManagementProperty, value);
    }

    public bool? Beutl_Api_Client
    {
        get => GetValue(Beutl_Api_ClientProperty);
        set => SetValue(Beutl_Api_ClientProperty, value);
    }
    
    public bool? Beutl_All_Errors
    {
        get => GetValue(Beutl_All_ErrorsProperty);
        set => SetValue(Beutl_All_ErrorsProperty, value);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}
