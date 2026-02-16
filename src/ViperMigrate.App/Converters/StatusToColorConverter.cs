using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ViperMigrate.Core.Models;
using ViperMigrate.Core.Restore;

namespace ViperMigrate.App.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CategoryStatus status)
        {
            return status switch
            {
                CategoryStatus.Pending => new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                CategoryStatus.InProgress => new SolidColorBrush(Color.FromRgb(0, 150, 255)),
                CategoryStatus.Success => new SolidColorBrush(Color.FromRgb(0, 200, 83)),
                CategoryStatus.PartialSuccess => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
                CategoryStatus.Failed => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                CategoryStatus.Skipped => new SolidColorBrush(Color.FromRgb(158, 158, 158)),
                _ => new SolidColorBrush(Colors.White)
            };
        }
        return new SolidColorBrush(Colors.White);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class StageStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is StageStatus status)
        {
            return status switch
            {
                StageStatus.Waiting => new SolidColorBrush(Color.FromRgb(128, 128, 128)),
                StageStatus.InProgress => new SolidColorBrush(Color.FromRgb(0, 150, 255)),
                StageStatus.Completed => new SolidColorBrush(Color.FromRgb(0, 200, 83)),
                StageStatus.PartiallyCompleted => new SolidColorBrush(Color.FromRgb(255, 193, 7)),
                StageStatus.Failed => new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                StageStatus.Skipped => new SolidColorBrush(Color.FromRgb(158, 158, 158)),
                _ => new SolidColorBrush(Colors.White)
            };
        }
        return new SolidColorBrush(Colors.White);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
