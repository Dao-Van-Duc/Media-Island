using Image = System.Windows.Controls.Image;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIslandMedia.Controls
{
    /// <summary>
    /// Reusable, centre-cropped cover with rounded clipping. The image itself is retained
    /// as an Image control so overlays, transitions and animations can be added later.
    /// </summary>
    public sealed class AlbumCover : Grid
    {
        private readonly Canvas _canvas;
        private readonly Image _image;

        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(AlbumCover),
                new PropertyMetadata(null, OnSourceChanged));

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(AlbumCover),
                new PropertyMetadata(8.0, OnAppearanceChanged));

        public ImageSource? Source
        {
            get => (ImageSource?)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public double CornerRadius
        {
            get => (double)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public AlbumCover()
        {
            ClipToBounds = true;
            _canvas = new Canvas { ClipToBounds = true };
            _image = new Image
            {
                Stretch = Stretch.Fill, // Aspect ratio is preserved by explicit dimensions.
                SnapsToDevicePixels = true,
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            _canvas.Children.Add(_image);
            Children.Add(_canvas);
            SizeChanged += (_, _) => UpdateLayoutForCover();
        }

        private static void OnSourceChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            var cover = (AlbumCover)target;
            cover._image.Source = (ImageSource?)args.NewValue;
            cover.UpdateLayoutForCover();
        }

        private static void OnAppearanceChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
        {
            ((AlbumCover)target).UpdateLayoutForCover();
        }

        private void UpdateLayoutForCover()
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            double radius = Math.Clamp(CornerRadius, 0, Math.Min(w, h) / 2.0);
            Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
            _canvas.Width = w;
            _canvas.Height = h;

            var source = Source;
            if (source == null || source.Width <= 0 || source.Height <= 0)
            {
                _image.Width = 0;
                _image.Height = 0;
                return;
            }

            // Explicit centre-cover geometry. Larger dimension is cropped equally on both sides.
            double scale = Math.Max(w / source.Width, h / source.Height);
            double imageWidth = source.Width * scale;
            double imageHeight = source.Height * scale;
            _image.Width = imageWidth;
            _image.Height = imageHeight;
            Canvas.SetLeft(_image, (w - imageWidth) / 2.0);
            Canvas.SetTop(_image, (h - imageHeight) / 2.0);
        }
    }
}
