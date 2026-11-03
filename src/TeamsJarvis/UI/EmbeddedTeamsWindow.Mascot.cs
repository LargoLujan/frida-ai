using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TeamsJarvis.UI;

// La mascota de Frida.ai (schnauzer): SIEMPRE visible. Habla desde un bocadillo (boca sincronizada
// con cada mensaje) y saca la patita con banderín verde/rojo del triaje. Cuando no pasa nada tiene
// vida propia: se da la vuelta sobre sí mismo, se come un hueso o saluda. Estilo videojuego.
public partial class EmbeddedTeamsWindow
{
    private DispatcherTimer? _idleReturn;    // vuelve a idle (oculta el bocadillo) tras hablar
    private DispatcherTimer? _mascotTalkStop; // cierra la boca al terminar cada "frase"
    private DispatcherTimer? _pawDown;        // baja la patita tras enseñar el banderín
    private DispatcherTimer? _idle;           // dispara las animaciones de reposo
    private DispatcherTimer? _boneEnd;        // termina el mordisqueo del hueso
    private DispatcherTimer? _hello;          // saludo inicial
    private bool _mascotBusy;
    private bool _mascotSticky; // hay un resultado importante en el bocadillo: idle no lo pisa
    private readonly Random _mascotRnd = new();

    private void InitMascot()
    {
        // Balanceo suave (siempre).
        MascotBob.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(0, 3, new Duration(TimeSpan.FromSeconds(1.1)))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });

        // Parpadeo cada ~3,2 s.
        var blink = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = new Duration(TimeSpan.FromSeconds(3.2)) };
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.00)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.90)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(0.1, KeyTime.FromPercent(0.95)));
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(1.00)));
        MascotEyeLScale.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
        MascotEyeRScale.BeginAnimation(ScaleTransform.ScaleYProperty, blink);

        // El bocadillo SIEMPRE está visible; este temporizador solo libera el "ocupado" para que
        // vuelvan las animaciones de reposo (el texto se mantiene hasta que haya uno nuevo).
        _idleReturn = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        _idleReturn.Tick += (_, _) => { _idleReturn!.Stop(); _mascotBusy = false; };

        _mascotTalkStop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0) };
        _mascotTalkStop.Tick += (_, _) => { _mascotTalkStop!.Stop(); SetMascotTalking(false); };

        _pawDown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
        _pawDown.Tick += (_, _) =>
        {
            _pawDown!.Stop();
            MascotFlagWave.BeginAnimation(RotateTransform.AngleProperty, null);
            MascotPawShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, 46, new Duration(TimeSpan.FromSeconds(0.35))) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseIn } });
        };

        _boneEnd = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _boneEnd.Tick += (_, _) =>
        {
            _boneEnd!.Stop();
            SetMascotTalking(false);
            var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromSeconds(0.25)));
            fade.Completed += (_, _) => MascotBoneShift.Y = 14;
            MascotBone.BeginAnimation(UIElement.OpacityProperty, fade);
        };

        // Bucle de reposo: cada 5-9 s hace una gracia si no está ocupado.
        _idle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _idle.Tick += (_, _) =>
        {
            _idle!.Interval = TimeSpan.FromSeconds(5 + _mascotRnd.NextDouble() * 4);
            if (_mascotBusy) return;
            switch (_mascotRnd.Next(3))
            {
                case 0: MascotSpin_(); break;
                case 1: MascotEatBone(); break;
                default: MascotGreet(); break;
            }
        };
        _idle.Start();

        // Saludo al aparecer.
        _hello = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
        _hello.Tick += (_, _) => { _hello!.Stop(); MascotGreet(); };
        _hello.Start();
    }

    /// <summary>Abre/cierra la boca en bucle para simular que habla/mordisquea.</summary>
    private void SetMascotTalking(bool on)
    {
        if (on)
            MascotMouthScale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.25, 1.0, new Duration(TimeSpan.FromSeconds(0.11)))
                { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        else
        {
            MascotMouthScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            MascotMouthScale.ScaleY = 0.25;
        }
        MascotBar.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// La mascota "dice" algo desde el bocadillo. La boca se mueve ~1 s por frase (sincronizada con el
    /// cambio de texto) y suena un ladrido suave (si está activado). El texto se REEMPLAZA en cada
    /// llamada (no se acumula): al final solo queda el resultado. Con <paramref name="keepTalking"/> el
    /// bocadillo no se auto-oculta (trabajo en curso).
    /// </summary>
    private void MascotSay(string text, bool keepTalking = false, bool sticky = false)
    {
        if (_idleReturn is null) return;
        _mascotBusy = true;
        _mascotSticky = sticky;
        ProgressText.Text = text;

        SetMascotTalking(true);
        _mascotTalkStop!.Stop();
        _mascotTalkStop.Start();

        BarkPlayer.Bark();

        _idleReturn.Stop();
        if (!keepTalking) _idleReturn.Start();
    }

    /// <summary>Fin del trabajo continuo: programa la vuelta a reposo.</summary>
    private void MascotDone()
    {
        _idleReturn?.Stop();
        _idleReturn?.Start();
    }

    /// <summary>Saca la patita con un banderín (verde = pasa triaje, rojo = no), con rebote.</summary>
    private void MascotShowFlag(bool pass)
    {
        MascotFlag.Fill = new SolidColorBrush(pass
            ? Color.FromRgb(0x2F, 0x9E, 0x44)   // verde
            : Color.FromRgb(0xD1, 0x39, 0x38)); // rojo

        MascotPawShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(46, 0, new Duration(TimeSpan.FromSeconds(0.45)))
            { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 } });
        MascotFlagWave.BeginAnimation(RotateTransform.AngleProperty,
            new DoubleAnimation(-11, 11, new Duration(TimeSpan.FromSeconds(0.16)))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });

        _pawDown!.Stop();
        _pawDown.Start();
    }

    // ---- Animaciones de reposo ------------------------------------------------

    /// <summary>Se da la vuelta sobre sí mismo (giro completo).</summary>
    private void MascotSpin_()
    {
        var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(0.7)))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        spin.Completed += (_, _) => { MascotSpin.BeginAnimation(RotateTransform.AngleProperty, null); MascotSpin.Angle = 0; };
        MascotSpin.BeginAnimation(RotateTransform.AngleProperty, spin);
    }

    /// <summary>Sale un hueso hacia la boca y lo mordisquea.</summary>
    private void MascotEatBone()
    {
        MascotBone.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromSeconds(0.18))));
        MascotBoneShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 2, new Duration(TimeSpan.FromSeconds(0.3))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        SetMascotTalking(true); // ñam ñam
        _boneEnd!.Stop();
        _boneEnd.Start();
    }

    /// <summary>Saluda: un pequeño meneo de cabeza + un "hola" breve.</summary>
    private void MascotGreet()
    {
        var wiggle = new DoubleAnimationUsingKeyFrames { Duration = new Duration(TimeSpan.FromSeconds(0.6)) };
        wiggle.KeyFrames.Add(new LinearDoubleKeyFrame(-14, KeyTime.FromPercent(0.2)));
        wiggle.KeyFrames.Add(new LinearDoubleKeyFrame(12, KeyTime.FromPercent(0.5)));
        wiggle.KeyFrames.Add(new LinearDoubleKeyFrame(-6, KeyTime.FromPercent(0.8)));
        wiggle.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1.0)));
        wiggle.Completed += (_, _) => { MascotSpin.BeginAnimation(RotateTransform.AngleProperty, null); MascotSpin.Angle = 0; };
        MascotSpin.BeginAnimation(RotateTransform.AngleProperty, wiggle);

        // No pisar un resultado importante que siga en el bocadillo.
        if (_mascotSticky) return;
        var saludos = new[] { "¡Hola! Aquí sigo.", "¿Sugerimos algo?", "Cuando quieras, pulsa «Sugerir respuesta».", "¡Guau! Listo para ayudar." };
        MascotSay(saludos[_mascotRnd.Next(saludos.Length)]);
    }
}
