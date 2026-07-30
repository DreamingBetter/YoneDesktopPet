using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;

namespace YoneDesktopPet
{
    public partial class MainWindow : Window
    {
        private const double MinPetHeight = 140;
        private const double MaxPetHeight = 560;

        private enum AttachmentMode
        {
            None,
            FloatingOnTop,
            ClingingLeft,
            ClingingRight,
            Falling
        }

        private readonly Random _random = new();
        private readonly string[] _voiceLines =
        {
            "一剑诛恶，一剑镇魂！",
            "疾风亦有归途",
            "双剑华斩！",
            "我就是给你的天罚！",
            "诛魔！",
            "人说谎言，剑说真相",
            "面具再多又有何用",
            "钢铁烈风！",
            "放弃，或是倒在我的剑下",
            "斩邪！",
            "你自称神王？谁的神？哪的王？",
            "神性解封！",
            "黎明与黄昏，合一！",
            "斩去谗言！",
            "剑刃狂澜！"
        };
        private readonly string[] _attachLines =
        {
            "疾风亦有归途"
        };
        private readonly string[] _descentLines =
        {
            "一剑诛恶，一剑镇魂！",
            "我就是给你的天罚！",
            "神性解封！",
            "黎明与黄昏，合一！"
        };

        private SpeechBubbleWindow? _bubbleWindow;
        private DispatcherTimer? _motionTimer;
        private DispatcherTimer? _animationTimer;
        private DispatcherTimer? _ambientSpeechTimer;
        private DispatcherTimer? _delayedClickSpeechTimer;
        private BitmapSource? _petBitmap;
        private BitmapSource? _flightLeftBitmap;
        private double _imageAspect = 1;
        private double _flightImageAspect = 1;
        private double _petHeight = 170;
        private double _anchorCenterX;
        private double _anchorBottomY;
        private double _animScaleX = 1;
        private double _animScaleY = 1;
        private double _animOffsetX;
        private double _animOffsetY;
        private double _animRotate;
        private double _lookX;
        private double _lookY;
        private double _dragVelocityX;
        private double _dragVelocityY;
        private double _dragPoseDirection = 1;
        private double _attachOffsetX;
        private double _attachOffsetY;
        private double _descentStartBottomY;
        private double _descentTargetBottomY;
        private double _descentDurationSeconds;
        private IntPtr _attachedWindow;
        private AttachmentMode _attachmentMode = AttachmentMode.None;
        private DateTimeOffset _idleStarted = DateTimeOffset.UtcNow;
        private DateTimeOffset _descentStartedAt;
        private DateTimeOffset _dragStartedAt;
        private DateTimeOffset _lastDragMotionAt;
        private DateTimeOffset _lastMotionFrameAt;
        private bool _isDragging;
        private bool _leftPressed;
        private Point _mouseDownScreen;
        private Point _lastDragScreen;
        private double _dragStartCenterX;
        private double _dragStartBottomY;

        public MainWindow()
        {
            InitializeComponent();

            _petBitmap = PetImageLoader.LoadTransparentCutout("Assets/yone.jpg");
            _flightLeftBitmap = PetImageLoader.LoadTransparentCutout("Assets/向左飞行.png");
            PetImage.Source = _petBitmap;
            _imageAspect = _petBitmap.PixelWidth / (double)_petBitmap.PixelHeight;
            _flightImageAspect = _flightLeftBitmap.PixelWidth / (double)_flightLeftBitmap.PixelHeight;
            PetImage.RenderTransform = new TransformGroup
            {
                Children =
                {
                    new ScaleTransform(),
                    new RotateTransform(),
                    new TranslateTransform()
                }
            };

            PetSurface.ContextMenu = BuildContextMenu();
            Loaded += OnLoaded;
            Closed += OnClosed;
            PetSurface.MouseLeftButtonDown += OnPetMouseLeftButtonDown;
            PetSurface.MouseMove += OnPetMouseMove;
            PetSurface.MouseLeftButtonUp += OnPetMouseLeftButtonUp;
            MouseWheel += OnMouseWheel;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _bubbleWindow = new SpeechBubbleWindow
            {
                Owner = this,
                Topmost = Topmost
            };

            var workArea = SystemParameters.WorkArea;
            var baseWidth = _petHeight * _imageAspect;
            _anchorCenterX = workArea.Right - baseWidth / 2 - 92;
            _anchorBottomY = workArea.Bottom - 48;
            ApplyPetLayout();
            StartMotionTimer();
            StartAmbientSpeechTimer();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _motionTimer?.Stop();
            _animationTimer?.Stop();
            _ambientSpeechTimer?.Stop();
            _delayedClickSpeechTimer?.Stop();
            _bubbleWindow?.Close();
        }

        private ContextMenu BuildContextMenu()
        {
            var menu = new ContextMenu();
            var sizeMenu = new MenuItem { Header = "调整大小" };
            sizeMenu.Items.Add(CreateSizeItem("小", 170));
            sizeMenu.Items.Add(CreateSizeItem("中", 260));
            sizeMenu.Items.Add(CreateSizeItem("大", 360));
            sizeMenu.Items.Add(CreateSizeItem("特大", 470));

            var topmostItem = new MenuItem
            {
                Header = "始终置顶",
                IsCheckable = true,
                IsChecked = true
            };
            topmostItem.Click += (_, _) =>
            {
                Topmost = topmostItem.IsChecked;
                if (_bubbleWindow != null)
                {
                    _bubbleWindow.Topmost = Topmost;
                }
            };

            var exitItem = new MenuItem { Header = "退出程序" };
            exitItem.Click += (_, _) => Close();

            menu.Items.Add(sizeMenu);
            menu.Items.Add(topmostItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(exitItem);
            return menu;
        }

        private MenuItem CreateSizeItem(string text, double height)
        {
            var item = new MenuItem { Header = text };
            item.Click += (_, _) => SetPetHeight(height);
            return item;
        }

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            SetPetHeight(_petHeight * (e.Delta > 0 ? 1.08 : 0.92));
            e.Handled = true;
        }

        private void SetPetHeight(double height)
        {
            _petHeight = Math.Clamp(height, MinPetHeight, MaxPetHeight);
            ApplyPetLayout();
        }

        private void OnPetMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _leftPressed = true;
            _isDragging = false;
            _mouseDownScreen = GetCursorScreenDip();
            _lastDragScreen = _mouseDownScreen;
            _lastDragMotionAt = DateTimeOffset.UtcNow;
            _dragStartedAt = _lastDragMotionAt;
            _dragPoseDirection = 1;
            _dragVelocityX = 0;
            _dragVelocityY = 0;
            _dragStartCenterX = _anchorCenterX;
            _dragStartBottomY = _anchorBottomY;
            PetSurface.CaptureMouse();
            e.Handled = true;
        }

        private void OnPetMouseMove(object sender, MouseEventArgs e)
        {
            if (!_leftPressed || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var current = GetCursorScreenDip();
            var delta = current - _mouseDownScreen;
            if (!_isDragging && Math.Abs(delta.X) + Math.Abs(delta.Y) > 4)
            {
                _isDragging = true;
                ClearAttachment();
                _delayedClickSpeechTimer?.Stop();
                _animationTimer?.Stop();
                ResetAnimationState();
                _lastDragScreen = current;
                _lastDragMotionAt = DateTimeOffset.UtcNow;
                _dragStartedAt = _lastDragMotionAt;
                _dragPoseDirection = delta.X < 0 ? -1 : 1;
            }

            if (!_isDragging)
            {
                return;
            }

            UpdateDragVelocity(current);
            _anchorCenterX = _dragStartCenterX + delta.X;
            _anchorBottomY = _dragStartBottomY + delta.Y;
            ApplyPetLayout();
        }

        private void OnPetMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (PetSurface.IsMouseCaptured)
            {
                PetSurface.ReleaseMouseCapture();
            }

            var wasDragging = _isDragging;
            _leftPressed = false;
            _isDragging = false;

            if (!wasDragging)
            {
                PlayLiftInteraction();
            }
            else
            {
                _dragVelocityX = 0;
                _dragVelocityY = 0;
                ApplyPetLayout();
                if (!TryAttachToNearbyWindow())
                {
                    PlayReleaseFloat();
                }
            }

            e.Handled = true;
        }

        private void UpdateDragVelocity(Point current)
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = Math.Max(0.016, (now - _lastDragMotionAt).TotalSeconds);
            var instantX = (current.X - _lastDragScreen.X) / elapsed;
            var instantY = (current.Y - _lastDragScreen.Y) / elapsed;

            _dragVelocityX = _dragVelocityX * 0.62 + instantX * 0.38;
            _dragVelocityY = _dragVelocityY * 0.62 + instantY * 0.38;
            if (Math.Abs(instantX) > 45)
            {
                _dragPoseDirection = instantX < 0 ? -1 : 1;
            }

            _lastDragScreen = current;
            _lastDragMotionAt = now;
        }

        private void PlayLiftInteraction()
        {
            ScheduleDelayedClickBubble(TimeSpan.FromMilliseconds(1380));
            RunAnimation(TimeSpan.FromMilliseconds(3200), p =>
            {
                const double peakHeight = 210;
                double height;

                if (p < 0.36)
                {
                    var t = p / 0.36;
                    height = peakHeight * EaseOutCubic(t);
                }
                else if (p < 0.50)
                {
                    var t = (p - 0.36) / 0.14;
                    height = peakHeight + Math.Sin(t * Math.PI * 2) * 5;
                }
                else
                {
                    var t = (p - 0.50) / 0.50;
                    height = peakHeight * (1 - EaseInOutSine(t));
                }

                var highPose = Math.Clamp(height / peakHeight, 0, 1);
                var landing = p > 0.88 ? Math.Sin((p - 0.88) / 0.12 * Math.PI) : 0;
                _animScaleX = 1 + 0.018 * highPose + 0.006 * landing;
                _animScaleY = 1 + 0.014 * highPose - 0.003 * landing;
                _animOffsetX = Math.Sin(p * Math.PI * 1.35) * 9 * highPose;
                _animOffsetY = -height;
                _animRotate = Math.Sin(p * Math.PI * 1.7) * 1.5 * highPose;
            }, useEasing: false, keepDelayedClickSpeech: true);
        }

        private void ScheduleDelayedClickBubble(TimeSpan delay)
        {
            _delayedClickSpeechTimer?.Stop();
            _delayedClickSpeechTimer = new DispatcherTimer
            {
                Interval = delay
            };
            _delayedClickSpeechTimer.Tick += (_, _) =>
            {
                _delayedClickSpeechTimer?.Stop();
                ShowClickBubble();
            };
            _delayedClickSpeechTimer.Start();
        }

        private void PlayReleaseFloat()
        {
            RunAnimation(TimeSpan.FromMilliseconds(460), p =>
            {
                var lift = Math.Sin(Math.PI * p);
                var fade = 1 - p;
                _animScaleX = 1 + 0.01 * lift;
                _animScaleY = 1 + 0.01 * lift;
                _animOffsetX = Math.Sin(Math.PI * 3 * p) * 5 * fade;
                _animOffsetY = -12 * lift;
                _animRotate = Math.Sin(Math.PI * 3 * p) * 1.8 * fade;
            });
        }

        private void RunAnimation(
            TimeSpan duration,
            Action<double> frame,
            bool useEasing = true,
            bool keepDelayedClickSpeech = false)
        {
            if (!keepDelayedClickSpeech)
            {
                _delayedClickSpeechTimer?.Stop();
            }

            _animationTimer?.Stop();
            var started = DateTimeOffset.UtcNow;
            _animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _animationTimer.Tick += (_, _) =>
            {
                var progress = (DateTimeOffset.UtcNow - started).TotalMilliseconds / duration.TotalMilliseconds;
                if (progress >= 1)
                {
                    _animationTimer?.Stop();
                    ResetAnimationState();
                    ApplyPetLayout();
                    return;
                }

                var boundedProgress = Math.Clamp(progress, 0, 1);
                frame(useEasing ? EaseInOutSine(boundedProgress) : boundedProgress);
                ApplyPetLayout();
            };
            _animationTimer.Start();
        }

        private void ResetAnimationState()
        {
            _animScaleX = 1;
            _animScaleY = 1;
            _animOffsetX = 0;
            _animOffsetY = 0;
            _animRotate = 0;
        }

        private void ShowClickBubble()
        {
            ShowVoiceBubble(TimeSpan.FromMilliseconds(2100));
        }

        private void ShowVoiceBubble(TimeSpan duration)
        {
            ShowVoiceBubble(duration, _voiceLines);
        }

        private void ShowVoiceBubble(TimeSpan duration, string[] lines)
        {
            if (_bubbleWindow == null)
            {
                return;
            }

            PositionBubble();
            var text = lines[_random.Next(lines.Length)];
            _bubbleWindow.ShowMessage(text, duration);
            PositionBubble();
        }

        private void StartAmbientSpeechTimer()
        {
            _ambientSpeechTimer = new DispatcherTimer();
            _ambientSpeechTimer.Tick += (_, _) =>
            {
                _ambientSpeechTimer.Stop();
                ShowAmbientBubble();
                ScheduleNextAmbientSpeech();
            };
            ScheduleNextAmbientSpeech();
        }

        private void ScheduleNextAmbientSpeech()
        {
            if (_ambientSpeechTimer == null)
            {
                return;
            }

            _ambientSpeechTimer.Interval = TimeSpan.FromSeconds(_random.Next(24, 56));
            _ambientSpeechTimer.Start();
        }

        private void ShowAmbientBubble()
        {
            if (_bubbleWindow == null || !IsVisible || _isDragging || _leftPressed)
            {
                return;
            }

            ShowVoiceBubble(TimeSpan.FromMilliseconds(2600));
        }

        private void ApplyPetLayout()
        {
            ApplyCurrentSprite();
            var petWidth = _petHeight * GetCurrentImageAspect();
            var petHeight = _petHeight;
            var padding = GetMotionPadding();

            PetSurface.Width = petWidth + padding * 2;
            PetSurface.Height = petHeight + padding * 2;
            PetImage.Width = petWidth;
            PetImage.Height = petHeight;
            Canvas.SetLeft(PetImage, padding);
            Canvas.SetTop(PetImage, padding);

            Width = PetSurface.Width;
            Height = PetSurface.Height;
            Left = _anchorCenterX - petWidth / 2 + _animOffsetX - padding;
            Top = _anchorBottomY - petHeight + _animOffsetY - padding;

            ApplyPetMotion(DateTimeOffset.UtcNow);
            PositionBubble();
        }

        private void ApplyCurrentSprite()
        {
            var source = IsFlightPoseActive() && _flightLeftBitmap != null ? _flightLeftBitmap : _petBitmap;
            if (source != null && PetImage.Source != source)
            {
                PetImage.Source = source;
            }
        }

        private bool IsFlightPoseActive()
        {
            return _isDragging && _flightLeftBitmap != null;
        }

        private double GetCurrentImageAspect()
        {
            return IsFlightPoseActive() ? _flightImageAspect : _imageAspect;
        }

        private void StartMotionTimer()
        {
            _idleStarted = DateTimeOffset.UtcNow;
            _lastMotionFrameAt = _idleStarted;
            _motionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            _motionTimer.Tick += (_, _) => UpdateMotion();
            _motionTimer.Start();
        }

        private void UpdateMotion()
        {
            if (!IsVisible)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var elapsed = Math.Max(0.001, (now - _lastMotionFrameAt).TotalSeconds);
            _lastMotionFrameAt = now;

            UpdateAttachedOrFalling(elapsed);
            UpdateLookTarget();
            ApplyPetLayout();
        }

        private void UpdateLookTarget()
        {
            double targetX;
            double targetY;
            if (_isDragging)
            {
                targetX = Math.Clamp(_dragVelocityX / 900, -1, 1);
                targetY = Math.Clamp(_dragVelocityY / 900, -1, 1);
            }
            else if (_attachmentMode == AttachmentMode.Falling)
            {
                targetX = Math.Sin((DateTimeOffset.UtcNow - _idleStarted).TotalSeconds * 5.5) * 0.7;
                targetY = 0.25;
            }
            else
            {
                var cursor = GetCursorScreenDip();
                var petBounds = GetVisiblePetBounds();
                var bodyCenter = new Point(petBounds.Left + petBounds.Width * 0.5, petBounds.Top + petBounds.Height * 0.45);
                targetX = Math.Clamp((cursor.X - bodyCenter.X) / 360, -1, 1);
                targetY = Math.Clamp((cursor.Y - bodyCenter.Y) / 320, -1, 1);
            }

            _lookX += (targetX - _lookX) * 0.12;
            _lookY += (targetY - _lookY) * 0.12;
        }

        private void ApplyPetMotion(DateTimeOffset now)
        {
            if (PetImage.RenderTransform is not TransformGroup group ||
                group.Children.Count < 3 ||
                group.Children[0] is not ScaleTransform scale ||
                group.Children[1] is not RotateTransform rotate ||
                group.Children[2] is not TranslateTransform translate)
            {
                return;
            }

            var sizeScale = _petHeight / 280;
            var seconds = (now - _idleStarted).TotalSeconds;
            var floatY = Math.Sin(seconds * 1.35) * 5.8;
            var driftX = Math.Sin(seconds * 0.78) * 3.2;
            var idleTilt = Math.Sin(seconds * 1.08) * 1.4;

            if (_isDragging)
            {
                var dragSeconds = (now - _dragStartedAt).TotalSeconds;
                var dragX = Math.Clamp(_dragVelocityX / 900, -1, 1);
                var dragY = Math.Clamp(_dragVelocityY / 900, -1, 1);
                var speed = Math.Clamp(Math.Sqrt(_dragVelocityX * _dragVelocityX + _dragVelocityY * _dragVelocityY) / 950, 0, 1);
                var wave = Math.Sin(dragSeconds * 12.0);
                var mirror = _dragPoseDirection > 0 ? -1 : 1;

                scale.ScaleX = mirror * _animScaleX * (1 + speed * 0.012);
                scale.ScaleY = _animScaleY * (1 + speed * 0.008);
                rotate.Angle = Math.Clamp(_dragPoseDirection * (7 + speed * 8) + wave * 5.5 + dragY * 3.0 + _animRotate, -24, 24);
                translate.X = Math.Clamp(_dragPoseDirection * 8 + wave * 4 - dragX * 4, -24, 24) * sizeScale;
                translate.Y = (-20 + Math.Abs(wave) * 3) * sizeScale;
                return;
            }

            if (_leftPressed)
            {
                scale.ScaleX = _animScaleX * 1.006;
                scale.ScaleY = _animScaleY * 1.006;
                rotate.Angle = _animRotate;
                translate.X = driftX * 0.35 * sizeScale;
                translate.Y = (-6 + floatY * 0.35) * sizeScale;
                return;
            }

            if (_attachmentMode == AttachmentMode.FloatingOnTop)
            {
                scale.ScaleX = _animScaleX;
                scale.ScaleY = _animScaleY;
                rotate.Angle = idleTilt * 0.55 + _lookX * 1.8 + _animRotate;
                translate.X = (driftX + _lookX * 2.4) * sizeScale;
                translate.Y = (floatY - 4 + _lookY * 1.2) * sizeScale;
                return;
            }

            if (_attachmentMode is AttachmentMode.ClingingLeft or AttachmentMode.ClingingRight)
            {
                var side = _attachmentMode == AttachmentMode.ClingingLeft ? 1 : -1;
                scale.ScaleX = _animScaleX;
                scale.ScaleY = _animScaleY;
                rotate.Angle = side * (7.5 + Math.Sin(seconds * 2.0) * 1.6) + _animRotate;
                translate.X = side * (5.5 + Math.Sin(seconds * 1.3) * 1.5) * sizeScale;
                translate.Y = (floatY * 0.8 + _lookY * 1.4) * sizeScale;
                return;
            }

            if (_attachmentMode == AttachmentMode.Falling)
            {
                scale.ScaleX = _animScaleX;
                scale.ScaleY = _animScaleY;
                rotate.Angle = Math.Sin(seconds * 0.95) * 1.8 + _lookX * 1.2 + _animRotate;
                translate.X = (Math.Sin(seconds * 0.72) * 3.8 + _lookX * 1.4) * sizeScale;
                translate.Y = (Math.Sin(seconds * 1.05) * 4.2 - 8 + _lookY * 1.0) * sizeScale;
                return;
            }

            scale.ScaleX = _animScaleX * (1 + Math.Sin(seconds * 1.1) * 0.004);
            scale.ScaleY = _animScaleY * (1 + Math.Cos(seconds * 1.1) * 0.004);
            rotate.Angle = idleTilt + _lookX * 3.5 + _lookY * 0.9 + _animRotate;
            translate.X = (driftX + _lookX * 3.8) * sizeScale;
            translate.Y = (floatY + _lookY * 2.2) * sizeScale;
        }

        private bool TryAttachToNearbyWindow()
        {
            var petBounds = GetVisiblePetBounds();
            var petCenterX = petBounds.Left + petBounds.Width / 2;
            var petCenterY = petBounds.Top + petBounds.Height / 2;
            var topThreshold = Math.Max(46, _petHeight * 0.24);
            var sideThreshold = Math.Max(46, _petHeight * 0.27);

            WindowAttachment? best = null;
            var workArea = SystemParameters.WorkArea;
            var zOrder = 0;
            foreach (var candidate in EnumerateAttachableWindows())
            {
                zOrder++;
                if (!TryGetWindowRectDip(candidate, out var rect))
                {
                    continue;
                }

                var topDistance = Math.Abs(petBounds.Bottom - rect.Top);
                var xOnTop = petCenterX >= rect.Left - petBounds.Width * 0.24 &&
                             petCenterX <= rect.Right + petBounds.Width * 0.24;
                var hasRoomAbove = rect.Top - petBounds.Height * 0.74 >= workArea.Top;
                if (hasRoomAbove && xOnTop && topDistance <= topThreshold)
                {
                    var score = topDistance + zOrder * 0.08;
                    best = ChooseBetter(best, new WindowAttachment(
                        candidate,
                        AttachmentMode.FloatingOnTop,
                        score,
                        petCenterX - rect.Left,
                        0));
                }

                var yOnSide = petCenterY >= rect.Top - petBounds.Height * 0.25 &&
                              petCenterY <= rect.Bottom + petBounds.Height * 0.25;
                if (!yOnSide)
                {
                    continue;
                }

                var leftSideDistance = Math.Abs(petBounds.Right - rect.Left);
                var hasRoomLeft = rect.Left - petBounds.Width * 0.58 >= workArea.Left;
                if (hasRoomLeft && leftSideDistance <= sideThreshold)
                {
                    var score = leftSideDistance + 12 + zOrder * 0.08;
                    best = ChooseBetter(best, new WindowAttachment(
                        candidate,
                        AttachmentMode.ClingingLeft,
                        score,
                        0,
                        petBounds.Bottom - rect.Top));
                }

                var rightSideDistance = Math.Abs(petBounds.Left - rect.Right);
                var hasRoomRight = rect.Right + petBounds.Width * 0.58 <= workArea.Right;
                if (hasRoomRight && rightSideDistance <= sideThreshold)
                {
                    var score = rightSideDistance + 12 + zOrder * 0.08;
                    best = ChooseBetter(best, new WindowAttachment(
                        candidate,
                        AttachmentMode.ClingingRight,
                        score,
                        0,
                        petBounds.Bottom - rect.Top));
                }
            }

            if (best == null)
            {
                return false;
            }

            AttachToWindow(best.Value);
            return true;
        }

        private static WindowAttachment ChooseBetter(WindowAttachment? current, WindowAttachment next)
        {
            return current == null || next.Score < current.Value.Score ? next : current.Value;
        }

        private void AttachToWindow(WindowAttachment attachment)
        {
            if (!TryGetWindowRectDip(attachment.Hwnd, out var rect))
            {
                return;
            }

            _attachedWindow = attachment.Hwnd;
            _attachmentMode = attachment.Mode;
            _attachOffsetX = attachment.OffsetX;
            _attachOffsetY = attachment.OffsetY;
            ResetDescentState();
            _animationTimer?.Stop();
            ResetAnimationState();
            FollowAttachedWindow(rect);
            PlayAttachCue(attachment.Mode);
        }

        private void PlayAttachCue(AttachmentMode mode)
        {
            ShowVoiceBubble(TimeSpan.FromMilliseconds(1700), _attachLines);
            RunAnimation(TimeSpan.FromMilliseconds(760), p =>
            {
                var settle = 1 - EaseOutCubic(p);
                var pulse = Math.Sin(Math.PI * p);
                _animScaleX = 1 + 0.020 * pulse;
                _animScaleY = 1 + 0.014 * pulse;

                if (mode == AttachmentMode.FloatingOnTop)
                {
                    _animOffsetX = Math.Sin(Math.PI * 2 * p) * 4 * (1 - p);
                    _animOffsetY = -34 * settle - 7 * pulse;
                    _animRotate = Math.Sin(Math.PI * 2 * p) * 2.2 * (1 - p);
                    return;
                }

                var side = mode == AttachmentMode.ClingingLeft ? 1 : -1;
                _animOffsetX = side * (34 * settle + Math.Sin(Math.PI * 2 * p) * 5 * (1 - p));
                _animOffsetY = -12 * settle - 4 * pulse;
                _animRotate = side * (5.0 * pulse + 2.0 * settle);
            }, useEasing: false);
        }

        private void ClearAttachment()
        {
            _attachedWindow = IntPtr.Zero;
            _attachmentMode = AttachmentMode.None;
            _attachOffsetX = 0;
            _attachOffsetY = 0;
            ResetDescentState();
        }

        private void UpdateAttachedOrFalling(double elapsedSeconds)
        {
            if (_isDragging || _leftPressed)
            {
                return;
            }

            if (_attachmentMode == AttachmentMode.Falling)
            {
                AdvanceFall(elapsedSeconds);
                return;
            }

            if (_attachedWindow == IntPtr.Zero)
            {
                return;
            }

            if (!TryGetWindowRectDip(_attachedWindow, out var rect))
            {
                BeginFall();
                return;
            }

            FollowAttachedWindow(rect);
        }

        private void FollowAttachedWindow(Rect rect)
        {
            var petWidth = _petHeight * GetCurrentImageAspect();
            var petHeight = _petHeight;
            var topOverlap = Math.Max(10, _petHeight * 0.055);
            var sideOverlap = Math.Max(22, _petHeight * 0.13);

            switch (_attachmentMode)
            {
                case AttachmentMode.FloatingOnTop:
                    var minX = rect.Left + petWidth * 0.32;
                    var maxX = rect.Right - petWidth * 0.32;
                    _anchorCenterX = Math.Clamp(rect.Left + _attachOffsetX, minX, maxX);
                    _anchorBottomY = rect.Top + topOverlap;
                    break;
                case AttachmentMode.ClingingLeft:
                    _anchorCenterX = rect.Left - petWidth / 2 + sideOverlap;
                    _anchorBottomY = Math.Clamp(rect.Top + _attachOffsetY, rect.Top + petHeight * 0.44, rect.Bottom + petHeight * 0.14);
                    break;
                case AttachmentMode.ClingingRight:
                    _anchorCenterX = rect.Right + petWidth / 2 - sideOverlap;
                    _anchorBottomY = Math.Clamp(rect.Top + _attachOffsetY, rect.Top + petHeight * 0.44, rect.Bottom + petHeight * 0.14);
                    break;
            }

            ApplyPetLayout();
        }

        private void BeginFall()
        {
            _attachedWindow = IntPtr.Zero;
            _attachmentMode = AttachmentMode.Falling;
            var floor = SystemParameters.WorkArea.Bottom - 46;
            _descentStartedAt = DateTimeOffset.UtcNow;
            _descentStartBottomY = _anchorBottomY;
            _descentTargetBottomY = floor;
            _descentDurationSeconds = Math.Clamp((floor - _anchorBottomY) / 280 + 1.25, 1.8, 4.2);
            _animationTimer?.Stop();
            ResetAnimationState();
            ShowVoiceBubble(TimeSpan.FromMilliseconds(2100), _descentLines);

            if (_anchorBottomY >= floor - 2)
            {
                _anchorBottomY = floor;
                _attachmentMode = AttachmentMode.None;
                ResetDescentState();
                PlayDivineLanding();
            }
        }

        private void AdvanceFall(double elapsedSeconds)
        {
            var elapsed = (DateTimeOffset.UtcNow - _descentStartedAt).TotalSeconds;
            var progress = Math.Clamp(elapsed / Math.Max(0.1, _descentDurationSeconds), 0, 1);
            var eased = EaseInOutSine(progress);
            _anchorBottomY = _descentStartBottomY + (_descentTargetBottomY - _descentStartBottomY) * eased;

            if (progress >= 1)
            {
                _anchorBottomY = _descentTargetBottomY;
                _attachmentMode = AttachmentMode.None;
                ResetDescentState();
                PlayDivineLanding();
                return;
            }

            ApplyPetLayout();
        }

        private void ResetDescentState()
        {
            _descentStartBottomY = 0;
            _descentTargetBottomY = 0;
            _descentDurationSeconds = 0;
            _descentStartedAt = DateTimeOffset.MinValue;
        }

        private void PlayDivineLanding()
        {
            RunAnimation(TimeSpan.FromMilliseconds(900), p =>
            {
                var settle = 1 - EaseOutCubic(p);
                var pulse = Math.Sin(Math.PI * p);
                _animScaleX = 1 + 0.012 * pulse;
                _animScaleY = 1 + 0.008 * pulse;
                _animOffsetX = Math.Sin(Math.PI * 2 * p) * 2.5 * (1 - p);
                _animOffsetY = -18 * settle - 4 * pulse;
                _animRotate = Math.Sin(Math.PI * p) * 1.2 * (1 - p);
            }, useEasing: false);
        }

        private void PositionBubble()
        {
            if (_bubbleWindow == null)
            {
                return;
            }

            _bubbleWindow.PositionNear(GetVisiblePetBounds());
        }

        private Rect GetVisiblePetBounds()
        {
            var padding = GetMotionPadding();
            return new Rect(Left + padding, Top + padding, Width - padding * 2, Height - padding * 2);
        }

        private double GetMotionPadding()
        {
            return Math.Max(48, _petHeight * 0.22);
        }

        private Point GetCursorScreenDip()
        {
            GetCursorPos(out var point);
            var screenPoint = new Point(point.X, point.Y);
            return DeviceToDip(screenPoint);
        }

        private Point DeviceToDip(Point screenPoint)
        {
            var source = PresentationSource.FromVisual(this);
            return source?.CompositionTarget == null
                ? screenPoint
                : source.CompositionTarget.TransformFromDevice.Transform(screenPoint);
        }

        private IEnumerable<IntPtr> EnumerateAttachableWindows()
        {
            var ownHandle = new WindowInteropHelper(this).Handle;
            var bubbleHandle = _bubbleWindow == null ? IntPtr.Zero : new WindowInteropHelper(_bubbleWindow).Handle;
            var desktopHandle = GetDesktopWindow();
            var shellHandle = GetShellWindow();
            var handles = new List<IntPtr>();

            EnumWindows((hWnd, _) =>
            {
                if (hWnd == IntPtr.Zero ||
                    hWnd == ownHandle ||
                    hWnd == bubbleHandle ||
                    hWnd == desktopHandle ||
                    hWnd == shellHandle)
                {
                    return true;
                }

                if (!IsWindowCandidate(hWnd))
                {
                    return true;
                }

                handles.Add(hWnd);
                return true;
            }, IntPtr.Zero);

            return handles;
        }

        private bool IsWindowCandidate(IntPtr hWnd)
        {
            if (!IsWindow(hWnd) || !IsWindowVisible(hWnd) || IsIconic(hWnd))
            {
                return false;
            }

            var className = GetClassNameText(hWnd);
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "NotifyIconOverflowWindow" or "Windows.UI.Core.CoreWindow")
            {
                return false;
            }

            return TryGetWindowRectDip(hWnd, out var rect) && rect.Width >= 140 && rect.Height >= 110;
        }

        private bool TryGetWindowRectDip(IntPtr hWnd, out Rect rect)
        {
            rect = Rect.Empty;
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd) || !IsWindowVisible(hWnd) || IsIconic(hWnd))
            {
                return false;
            }

            if (!GetWindowRect(hWnd, out var nativeRect))
            {
                return false;
            }

            var topLeft = DeviceToDip(new Point(nativeRect.Left, nativeRect.Top));
            var bottomRight = DeviceToDip(new Point(nativeRect.Right, nativeRect.Bottom));
            rect = new Rect(topLeft, bottomRight);
            return rect.Width >= 80 && rect.Height >= 80;
        }

        private static string GetClassNameText(IntPtr hWnd)
        {
            var builder = new StringBuilder(256);
            GetClassName(hWnd, builder, builder.Capacity);
            return builder.ToString();
        }

        private static double EaseInOutSine(double x)
        {
            return -(Math.Cos(Math.PI * x) - 1) / 2;
        }

        private static double EaseOutCubic(double x)
        {
            return 1 - Math.Pow(1 - x, 3);
        }

        private readonly struct WindowAttachment
        {
            public WindowAttachment(IntPtr hwnd, AttachmentMode mode, double score, double offsetX, double offsetY)
            {
                Hwnd = hwnd;
                Mode = mode;
                Score = score;
                OffsetX = offsetX;
                OffsetY = offsetY;
            }

            public IntPtr Hwnd { get; }
            public AttachmentMode Mode { get; }
            public double Score { get; }
            public double OffsetX { get; }
            public double OffsetY { get; }
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }

    internal sealed class SpeechBubbleWindow : Window
    {
        private readonly TextBlock _textBlock;
        private readonly DispatcherTimer _hideTimer;

        public SpeechBubbleWindow()
        {
            Width = 230;
            Height = 78;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;

            var root = new Grid
            {
                Background = Brushes.Transparent
            };

            var bubble = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(219, 224, 236)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(15, 9, 15, 11),
                Margin = new Thickness(0, 0, 0, 9)
            };

            _textBlock = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(32, 35, 50)),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            bubble.Child = _textBlock;
            root.Children.Add(bubble);

            var tail = new System.Windows.Shapes.Polygon
            {
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(219, 224, 236)),
                StrokeThickness = 1,
                Points = new PointCollection
                {
                    new(106, 66),
                    new(124, 66),
                    new(115, 78)
                }
            };
            root.Children.Add(tail);
            Content = root;

            _hideTimer = new DispatcherTimer();
            _hideTimer.Tick += (_, _) =>
            {
                _hideTimer.Stop();
                Hide();
            };
        }

        public void ShowMessage(string text, TimeSpan duration)
        {
            _textBlock.Text = text;
            _hideTimer.Stop();
            _hideTimer.Interval = duration;
            if (!IsVisible)
            {
                Show();
            }
            _hideTimer.Start();
        }

        public void PositionNear(Rect petBounds)
        {
            var screen = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            const double gap = 10;
            var left = petBounds.Left + petBounds.Width / 2 - Width / 2;
            var top = petBounds.Top - Height - gap;

            if (top < screen.Top + gap)
            {
                top = petBounds.Bottom + gap;
            }

            if (top + Height > screen.Bottom - gap)
            {
                top = petBounds.Top + petBounds.Height * 0.32 - Height / 2;
                left = petBounds.Right + gap;

                if (left + Width > screen.Right - gap)
                {
                    left = petBounds.Left - Width - gap;
                }
            }

            left = Math.Clamp(left, screen.Left + gap, screen.Right - Width - gap);
            top = Math.Clamp(top, screen.Top + gap, screen.Bottom - Height - gap);

            Left = left;
            Top = top;
        }
    }

    internal static class PetImageLoader
    {
        public static BitmapSource LoadTransparentCutout(string resourcePath)
        {
            var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/{resourcePath}"));
            if (resource == null)
            {
                throw new InvalidOperationException($"找不到桌宠素材资源：{resourcePath}");
            }

            using var stream = resource.Stream;
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var source = decoder.Frames[0];
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            var width = converted.PixelWidth;
            var height = converted.PixelHeight;
            var stride = width * 4;
            var pixels = new byte[stride * height];
            converted.CopyPixels(pixels, stride, 0);

            var transparent = FindConnectedBackground(pixels, width, height);
            for (var i = 0; i < transparent.Length; i++)
            {
                if (transparent[i])
                {
                    pixels[i * 4 + 3] = 0;
                }
            }

            return CropTransparentBounds(pixels, width, height, converted.DpiX, converted.DpiY);
        }

        private static bool[] FindConnectedBackground(byte[] pixels, int width, int height)
        {
            var transparent = new bool[width * height];
            var queue = new Queue<int>();

            void TryAdd(int x, int y)
            {
                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    return;
                }

                var index = y * width + x;
                if (transparent[index] || !IsBackgroundPixel(pixels, index))
                {
                    return;
                }

                transparent[index] = true;
                queue.Enqueue(index);
            }

            for (var x = 0; x < width; x++)
            {
                TryAdd(x, 0);
                TryAdd(x, height - 1);
            }

            for (var y = 1; y < height - 1; y++)
            {
                TryAdd(0, y);
                TryAdd(width - 1, y);
            }

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var x = index % width;
                var y = index / width;
                TryAdd(x - 1, y);
                TryAdd(x + 1, y);
                TryAdd(x, y - 1);
                TryAdd(x, y + 1);
            }

            for (var pass = 0; pass < 2; pass++)
            {
                var fringe = new List<int>();
                for (var y = 1; y < height - 1; y++)
                {
                    for (var x = 1; x < width - 1; x++)
                    {
                        var index = y * width + x;
                        if (transparent[index] || !IsFringePixel(pixels, index))
                        {
                            continue;
                        }

                        if (transparent[index - 1] || transparent[index + 1] ||
                            transparent[index - width] || transparent[index + width])
                        {
                            fringe.Add(index);
                        }
                    }
                }

                foreach (var index in fringe)
                {
                    transparent[index] = true;
                }
            }

            return transparent;
        }

        private static bool IsBackgroundPixel(byte[] pixels, int pixelIndex)
        {
            var offset = pixelIndex * 4;
            var b = pixels[offset];
            var g = pixels[offset + 1];
            var r = pixels[offset + 2];
            return Math.Max(r, Math.Max(g, b)) <= 18;
        }

        private static bool IsFringePixel(byte[] pixels, int pixelIndex)
        {
            var offset = pixelIndex * 4;
            var b = pixels[offset];
            var g = pixels[offset + 1];
            var r = pixels[offset + 2];
            return r <= 30 && g <= 30 && b <= 30;
        }

        private static BitmapSource CropTransparentBounds(byte[] pixels, int width, int height, double dpiX, double dpiY)
        {
            var minX = width;
            var minY = height;
            var maxX = -1;
            var maxY = -1;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var alpha = pixels[(y * width + x) * 4 + 3];
                    if (alpha == 0)
                    {
                        continue;
                    }

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }

            if (maxX < minX || maxY < minY)
            {
                throw new InvalidOperationException("图片没有可显示的角色内容。");
            }

            minX = Math.Max(0, minX - 2);
            minY = Math.Max(0, minY - 2);
            maxX = Math.Min(width - 1, maxX + 2);
            maxY = Math.Min(height - 1, maxY + 2);

            var cropWidth = maxX - minX + 1;
            var cropHeight = maxY - minY + 1;
            var cropStride = cropWidth * 4;
            var cropped = new byte[cropStride * cropHeight];

            for (var y = 0; y < cropHeight; y++)
            {
                Buffer.BlockCopy(
                    pixels,
                    ((minY + y) * width + minX) * 4,
                    cropped,
                    y * cropStride,
                    cropStride);
            }

            var bitmap = BitmapSource.Create(
                cropWidth,
                cropHeight,
                dpiX,
                dpiY,
                PixelFormats.Bgra32,
                null,
                cropped,
                cropStride);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
