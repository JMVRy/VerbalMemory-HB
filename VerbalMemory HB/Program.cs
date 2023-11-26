using System.Runtime.InteropServices;   // DllImportAttribute

using ScreenCapture.NET;                // DX11ScreenCapture, CaptureZone, ColorBGRA, etc.
 
using Emgu.CV;                          // Image
using Emgu.CV.OCR;                      // Tesseract, PageSegMode
using Emgu.CV.Structure;                // Bgra

// Defines the OCR tools so that we can read the words
// Using English training data (all words in the test are English), LSTM+Tesseract for max accuracy, and only using letters
var ocr = new Tesseract( Tesseract.DefaultTesseractDirectory /* ./tessdata/ folder */, "eng", OcrEngineMode.TesseractLstmCombined, "abcdefghijklmnopqrstuvwxyz" )
{
    PageSegMode = PageSegMode.SingleWord // Self explanatory, all it needs to do is read a single word
};

// This is where we store each word we see, so we can look back on it for whether we already saw the word
// Could've used a HashSet<string> instead, but a List works fine. It's not like we need to run it for so long that a HashSet would have significant benefits
List<string> words = new();

// Defines where the buttons we need to click are
// Both values should be changed for your own system, as this is for my specific monitor arrangement
MouseOperations.MousePoint seen    = new( -1035, 636 );
MouseOperations.MousePoint newWord = new(  -900, 635 );

// Use DX11 to capture the screen of the last display on my first GPU
DX11ScreenCaptureService screenCaptureService = new DX11ScreenCaptureService();
IEnumerable<GraphicsCard> graphicsCards = screenCaptureService.GetGraphicsCards();
IEnumerable<Display> displays = screenCaptureService.GetDisplays( graphicsCards.First() );
DX11ScreenCapture screenCapture = screenCaptureService.GetScreenCapture( displays.Last() );

// We only need to capture the box that would contain any word that appears
// Just in case a word is exceptionally long, we make the width wider than required. It doesn't significantly affect performance or accuracy, so it's fine
CaptureZone<ColorBGRA> captureZone = screenCapture.RegisterCaptureZone( 760, 400, 400, 50 );

// The first frame will always be black, so it's preemptively removed
screenCapture.CaptureScreen();

// Constantly loop, so this will go on forever until told otherwise (Ctrl+C or closing the console)
while (true)
{
    screenCapture.CaptureScreen();

    // Lock image data to access the data within
    using (captureZone.Lock())
    {
        // Turn captured image into pure byte data so EmguCV can utilize it
        // Personally, I directly specify the type of data used (i.e. RefImage<ColorBGRA>) rather than using "var". This is personal preference, and can be changed for better readability
        RefImage<ColorBGRA> image = captureZone.Image;
        ColorBGRA[] colors = image.ToArray();

        byte[] data = new byte[colors.Length * 4];
        for (int i = 0; i < colors.Length; i++)
        {
            data[i * 4] = colors[i].B;
            data[i * 4 + 1] = colors[i].G;
            data[i * 4 + 2] = colors[i].R;
            data[i * 4 + 3] = colors[i].A;
        }

        // Define a new image with the given width and height, based on the pure byte data from above
        Image<Bgra, byte> emguImage = new( image.Width, image.Height );
        emguImage.Bytes = data;

        // Gives Tesseract the image, and if it can't recognize any characters (non-zero result), say it errored.
        ocr.SetImage( emguImage );
        if (ocr.Recognize() != 0) Console.WriteLine( "Errored!" );

        // Get the text that it recognized
        string word = ocr.GetUTF8Text();

        // If the wordlist has a matching word, then click the seen button. Otherwise, it's a new word and gets added to the wordlist.
        // Conveniently, we don't even need to be completely accurate. As long as the same word is read the same way, it'll match
        if (words.Contains( word ) )
        {
            Console.WriteLine( $"Match! {word}" );

            MouseOperations.SetCursorPosition( seen );
            MouseOperations.MouseEvent( MouseOperations.MouseEventFlags.LeftDown );
            MouseOperations.MouseEvent( MouseOperations.MouseEventFlags.LeftUp );
        }
        else
        {
            Console.WriteLine($"New word, {word}");
            words.Add( word );

            MouseOperations.SetCursorPosition( newWord );
            MouseOperations.MouseEvent( MouseOperations.MouseEventFlags.LeftDown );
            MouseOperations.MouseEvent( MouseOperations.MouseEventFlags.LeftUp );
        }
    }

    // Wait a second, both to allow for quickly closing 
    Thread.Sleep( 1000 );
}

// Outdated input classes, updated version at https://github.com/JMVRy/Win32-Input
#region Input Operation Classes
public class MouseOperations
{
    [Flags]
    public enum MouseEventFlags
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        MiddleDown = 0x0020,
        MiddleUp = 0x0040,
        Move = 0x0001,
        Absolute = 0x8000,
        RightDown = 0x0008,
        RightUp = 0x0010
    }

    [DllImport( "user32.dll", EntryPoint = "SetCursorPos" )]
    [return: MarshalAs( UnmanagedType.Bool )]
    private static extern bool SetCursorPos( int x, int y );

    [DllImport( "user32.dll" )]
    [return: MarshalAs( UnmanagedType.Bool )]
    private static extern bool GetCursorPos( out MousePoint lpMousePoint );

    [DllImport( "user32.dll" )]
    private static extern void mouse_event( int dwFlags, int dx, int dy, int dwData, int dwExtraInfo );

    [DllImport( "user32.dll" )]
    private static extern IntPtr GetDC( IntPtr hWnd );

    [DllImport( "user32.dll" )]
    private static extern int ReleaseDC( IntPtr hWnd, IntPtr hdc );

    [DllImport( "gdi32.dll", EntryPoint = "GetPixel" )]
    private static extern uint GetPixel( IntPtr hdc, int x, int y );

    [DllImport( "user32.dll", EntryPoint = "FindWindow", SetLastError = true )]
    private static extern IntPtr FindWindowByCaption( IntPtr ZeroOnly, string lpWindowName );

    public static IntPtr FindWindowByCaption( string caption ) => FindWindowByCaption( IntPtr.Zero, caption );

    public static Color GetPixelColor( MousePoint mousePoint ) => GetPixelColor( IntPtr.Zero, mousePoint );
    public static Color GetPixelColor( IntPtr hWnd, MousePoint mousePoint )
    {
        IntPtr hdc = GetDC( hWnd );
        uint pixel = GetPixel( hdc, mousePoint.X, mousePoint.Y );
        ReleaseDC( hWnd, hdc );

        Color color = new Color(
            ( byte )((pixel & 0x000000FF) >> 0),
            ( byte )((pixel & 0x0000FF00) >> 8),
            ( byte )((pixel & 0x00FF0000) >> 16),
            ( byte )((pixel & 0xFF000000) >> 24)
        );
        return color;
    }

    public static Color GetPixelColor( int x, int y ) => GetPixelColor( IntPtr.Zero, x, y );
    public static Color GetPixelColor( IntPtr hWnd, int x, int y )
    {
        IntPtr hdc = GetDC( hWnd );
        uint pixel = GetPixel( hdc, x, y );
        ReleaseDC( hWnd, hdc );

        Color color = new Color(
            ( byte )((pixel & 0x000000FF) >> 0),
            ( byte )((pixel & 0x0000FF00) >> 8),
            ( byte )((pixel & 0x00FF0000) >> 16),
            ( byte )((pixel & 0xFF000000) >> 24)
        );
        return color;
    }

    public static void SetCursorPosition( int x, int y )
    {
        SetCursorPos( x, y );
    }

    public static void SetCursorPosition( MousePoint point )
    {
        SetCursorPos( point.X, point.Y );
    }

    public static MousePoint GetCursorPosition()
    {
        MousePoint currentMousePoint;

        var gotPoint = GetCursorPos( out currentMousePoint );
        if (!gotPoint)
            currentMousePoint = new MousePoint( 0, 0 );

        return currentMousePoint;
    }

    public static void MouseEvent( MouseEventFlags value )
    {
        MousePoint position = GetCursorPosition();

        mouse_event( ( int )value, position.X, position.Y, 0, 0 );
    }

    [StructLayout( LayoutKind.Sequential )]
    public struct MousePoint
    {
        public int X;
        public int Y;

        public MousePoint( int x, int y )
        {
            X = x;
            Y = y;
        }

        public static bool operator !=( MousePoint left, MousePoint right ) => !(left == right);
        public static bool operator ==( MousePoint left, MousePoint right )
        {
            if (left.X == right.X && left.Y == right.Y) return true;
            return false;
        }

        public override readonly bool Equals( object? obj )
        {
            if (obj == null) return false;
            return this == ( MousePoint )obj;
        }

        public override string ToString()
        {
            return (X, Y).ToString();
        }

        public override int GetHashCode()
        {
            return (X, Y).GetHashCode();
        }
    }

    public struct Color
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public Color( byte r, byte g, byte b, byte a )
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public Color() : this( 0, 0, 0, 0 ) { }

        public static bool operator !=( Color left, Color right ) => !(left == right);
        public static bool operator ==( Color left, Color right )
        {
            if (left.R == right.R && left.G == right.G && left.B == right.B && left.A == right.A) return true;
            return false;
        }

        public override readonly bool Equals( object? obj )
        {
            if (obj == null) return false;
            return this == ( Color )obj;
        }

        public override string ToString()
        {
            return (R, G, B, A).ToString();
        }

        public override int GetHashCode()
        {
            return (R, G, B, A).GetHashCode();
        }
    }
}

public class InputOperations
{
    [DllImport( "user32.dll", SetLastError = true )]
    private static extern uint SendInput( uint cInputs, INPUT[] pInputs, int cbSize );

    [DllImport( "user32.dll", SetLastError = true )]
    private static extern IntPtr GetMessageExtraInfo();

    public static uint InputEvent( INPUT[] inputs )
    {
        // Extra processing
        // ...

        // Make sure all extra info is included
        for (int i = 0; i < inputs.Length; i++)
        {
            //Console.WriteLine($"[InputEvent] Got Input: { inputs[i].U.ki.wVk }");
            inputs[i].U.mi.dwExtraInfo = ( UIntPtr )GetMessageExtraInfo();
            inputs[i].U.ki.dwExtraInfo = ( UIntPtr )GetMessageExtraInfo();
        }

        //Console.WriteLine($"[InputEvent] Sending {inputs.Length} inputs, INPUT.Size = {INPUT.Size}, sizeof(INPUT) = {sizeof(INPUT)}");
        return SendInput( ( uint )inputs.Length, inputs, INPUT.Size );
    }

    public static uint SendKeypress( VirtualKeyShort vk, uint millisecondDelay = 0 ) => SendKeypress( new VirtualKeyShort[] { vk }, millisecondDelay );
    public static uint SendKeypress( VirtualKeyShort[] virtualKeys, uint millisecondDelay = 0 )
    {
        //Console.WriteLine($"[SendKeypress] Input length: {virtualKeys.Length}");
        INPUT[] inputs = new INPUT[virtualKeys.Length];

        for (int i = 0; i < inputs.Length; i++)
        {
            //Console.WriteLine($"[SendKeypress] Input[{i}]{{wVk = {virtualKeys[i]}}}.KeyDown");
            inputs[i].type = InputType.INPUT_KEYBOARD;
            inputs[i].U.ki.wScan = 0;
            inputs[i].U.ki.time = 0;
            inputs[i].U.ki.dwFlags = 0;
            inputs[i].U.ki.wVk = virtualKeys[i];
        }

        //Console.WriteLine("[SendKeypress] Sending normal inputs");
        InputEvent( inputs );

        //Console.WriteLine("[SendKeypress] Creating delay task");
        Task delay = Task.CompletedTask;
        if (millisecondDelay > 0)
            delay = Task.Delay( ( int )millisecondDelay );

        for (int i = 0; i < inputs.Length; i++)
        {
            //Console.WriteLine($"[SendKeypress] Input[{i}].KeyUp");
            inputs[i].U.ki.dwFlags = KEYEVENTF.KEYUP;
        }

        //Console.WriteLine($"[SendKeypress] Waiting for delay, ended too early = {delay.IsCompleted}, status = {delay.Status}");
        delay.Wait();

        //Console.WriteLine("[SendKeypress] Sending KeyUp inputs");
        return InputEvent( inputs );
    }

    #region Input Data
    [StructLayout( LayoutKind.Sequential )]
    public struct INPUT
    {
        public InputType type;
        public InputUnion U;
        public static int Size
        {
            get { return Marshal.SizeOf( typeof( INPUT ) ); }
        }
    }

    public enum InputType : uint
    {
        INPUT_MOUSE,
        INPUT_KEYBOARD,
        INPUT_HARDWARE
    }

    [StructLayout( LayoutKind.Explicit )]
    public struct InputUnion
    {
        [FieldOffset( 0 )]
        public MOUSEINPUT mi;
        [FieldOffset( 0 )]
        public KEYBDINPUT ki;
        [FieldOffset( 0 )]
        public HARDWAREINPUT hi;
    }

    #region Hardware
    [StructLayout( LayoutKind.Sequential )]
    public struct HARDWAREINPUT
    {
        internal int uMsg;
        internal short wParamL;
        internal short wParamR;
    }
    #endregion

    #region Keyboard
    [StructLayout( LayoutKind.Sequential )]
    public struct KEYBDINPUT
    {
        internal VirtualKeyShort wVk;
        internal ScanCodeShort wScan;
        internal KEYEVENTF dwFlags;
        internal int time;
        internal UIntPtr dwExtraInfo;
    }

    [Flags]
    public enum KEYEVENTF : uint
    {
        EXTENDEDKEY = 0x1,
        KEYUP = 0x2,
        UNICODE = 0x4,
        SCANCODE = 0x8,
    }

    public enum ScanCodeShort : short
    {
        VK_LBUTTON = 0,
        VK_RBUTTON = 0,
        VK_CANCEL = 70,
        VK_MBUTTON = 0,
        VK_XBUTTON1 = 0,
        VK_XBUTTON2 = 0,
        VK_BACK = 14,
        VK_TAB = 15,
        VK_CLEAR = 76,
        VK_RETURN = 28,
        VK_SHIFT = 42,
        VK_CONTROL = 29,
        VK_MENU = 56,
        VK_PAUSE = 0,
        VK_CAPITAL = 58,
        VK_KANA = 0,
        VK_HANGUL = 0,
        VK_JUNJA = 0,
        VK_FINAL = 0,
        VK_HANJA = 0,
        VK_KANJI = 0,
        VK_ESCAPE = 1,
        VK_CONVERT = 0,
        VK_NONCONVERT = 0,
        VK_ACCEPT = 0,
        VK_MODECHANGE = 0,
        VK_SPACE = 57,
        VK_PRIOR = 73,
        VK_NEXT = 81,
        VK_END = 79,
        VK_HOME = 71,
        VK_LEFT = 75,
        VK_UP = 72,
        VK_RIGHT = 77,
        VK_DOWN = 80,
        VK_SELECT = 0,
        VK_PRINT = 0,
        VK_EXECUTE = 0,
        VK_SNAPSHOT = 84,
        VK_INSERT = 82,
        VK_DELETE = 83,
        VK_HELP = 99,
        VK_KEY_0 = 11,
        VK_KEY_1 = 2,
        VK_KEY_2 = 3,
        VK_KEY_3 = 4,
        VK_KEY_4 = 5,
        VK_KEY_5 = 6,
        VK_KEY_6 = 7,
        VK_KEY_7 = 8,
        VK_KEY_8 = 9,
        VK_KEY_9 = 10,
        VK_KEY_A = 30,
        VK_KEY_B = 48,
        VK_KEY_C = 46,
        VK_KEY_D = 32,
        VK_KEY_E = 18,
        VK_KEY_F = 33,
        VK_KEY_G = 34,
        VK_KEY_H = 35,
        VK_KEY_I = 23,
        VK_KEY_J = 36,
        VK_KEY_K = 37,
        VK_KEY_L = 38,
        VK_KEY_M = 50,
        VK_KEY_N = 49,
        VK_KEY_O = 24,
        VK_KEY_P = 25,
        VK_KEY_Q = 16,
        VK_KEY_R = 19,
        VK_KEY_S = 31,
        VK_KEY_T = 20,
        VK_KEY_U = 22,
        VK_KEY_V = 47,
        VK_KEY_W = 17,
        VK_KEY_X = 45,
        VK_KEY_Y = 21,
        VK_KEY_Z = 44,
        VK_LWIN = 91,
        VK_RWIN = 92,
        VK_APPS = 93,
        VK_SLEEP = 95,
        VK_NUMPAD0 = 82,
        VK_NUMPAD1 = 79,
        VK_NUMPAD2 = 80,
        VK_NUMPAD3 = 81,
        VK_NUMPAD4 = 75,
        VK_NUMPAD5 = 76,
        VK_NUMPAD6 = 77,
        VK_NUMPAD7 = 71,
        VK_NUMPAD8 = 72,
        VK_NUMPAD9 = 73,
        VK_MULTIPLY = 55,
        VK_ADD = 78,
        VK_SEPARATOR = 0,
        VK_SUBTRACT = 74,
        VK_DECIMAL = 83,
        VK_DIVIDE = 53,
        VK_F1 = 59,
        VK_F2 = 60,
        VK_F3 = 61,
        VK_F4 = 62,
        VK_F5 = 63,
        VK_F6 = 64,
        VK_F7 = 65,
        VK_F8 = 66,
        VK_F9 = 67,
        VK_F10 = 68,
        VK_F11 = 87,
        VK_F12 = 88,
        VK_F13 = 100,
        VK_F14 = 101,
        VK_F15 = 102,
        VK_F16 = 103,
        VK_F17 = 104,
        VK_F18 = 105,
        VK_F19 = 106,
        VK_F20 = 107,
        VK_F21 = 108,
        VK_F22 = 109,
        VK_F23 = 110,
        VK_F24 = 118,
        VK_NUMLOCK = 69,
        VK_SCROLL = 70,
        VK_LSHIFT = 42,
        VK_RSHIFT = 54,
        VK_LCONTROL = 29,
        VK_RCONTROL = 29,
        VK_LMENU = 56,
        VK_RMENU = 56,
        VK_BROWSER_BACK = 106,
        VK_BROWSER_FORWARD = 105,
        VK_BROWSER_REFRESH = 103,
        VK_BROWSER_STOP = 104,
        VK_BROWSER_SEARCH = 101,
        VK_BROWSER_FAVORITES = 102,
        VK_BROWSER_HOME = 50,
        VK_VOLUME_MUTE = 32,
        VK_VOLUME_DOWN = 46,
        VK_VOLUME_UP = 48,
        VK_MEDIA_NEXT_TRACK = 25,
        VK_MEDIA_PREV_TRACK = 16,
        VK_MEDIA_STOP = 36,
        VK_MEDIA_PLAY_PAUSE = 34,
        VK_LAUNCH_MAIL = 108,
        VK_LAUNCH_MEDIA_SELECT = 109,
        VK_LAUNCH_APP1 = 107,
        VK_LAUNCH_APP2 = 33,
        VK_OEM_1 = 39,
        VK_OEM_PLUS = 13,
        VK_OEM_COMMA = 51,
        VK_OEM_MINUS = 12,
        VK_OEM_PERIOD = 52,
        VK_OEM_2 = 53,
        VK_OEM_3 = 41,
        VK_OEM_4 = 26,
        VK_OEM_5 = 43,
        VK_OEM_6 = 27,
        VK_OEM_7 = 40,
        VK_OEM_8 = 0,
        VK_OEM_102 = 86,
        VK_PROCESSKEY = 0,
        VK_PACKET = 0,
        VK_ATTN = 0,
        VK_CRSEL = 0,
        VK_EXSEL = 0,
        VK_EREOF = 93,
        VK_PLAY = 0,
        VK_ZOOM = 98,
        VK_NONAME = 0,
        VK_PA1 = 0,
        VK_OEM_CLEAR = 0,
    }

    public enum VirtualKeyShort : short
    {
        /// <summary>
        /// Left mouse button
        /// </summary>
        VK_LBUTTON = 0x01,

        /// <summary>
        /// Right mouse button
        /// </summary>
        VK_RBUTTON = 0x02,

        /// <summary>
        /// Control-break processing
        /// </summary>
        VK_CANCEL = 0x03,

        /// <summary>
        /// Middle mouse button
        /// </summary>
        VK_MBUTTON = 0x04,

        /// <summary>
        /// X1 mouse button
        /// </summary>
        VK_XBUTTON1 = 0x05,

        /// <summary>
        /// X2 mouse button
        /// </summary>
        VK_XBUTTON2 = 0x06,

        /// <summary>
        /// BACKSPACE key
        /// </summary>
        VK_BACK = 0x08,

        /// <summary>
        /// TAB key
        /// </summary>
        VK_TAB = 0x09,

        /// <summary>
        /// CLEAR key
        /// </summary>
        VK_CLEAR = 0x0C,

        /// <summary>
        /// ENTER key
        /// </summary>
        VK_RETURN = 0x0D,

        /// <summary>
        /// SHIFT key
        /// </summary>
        VK_SHIFT = 0x10,

        /// <summary>
        /// CTRL key
        /// </summary>
        VK_CONTROL = 0x11,

        /// <summary>
        /// ALT key
        /// </summary>
        VK_MENU = 0x12,

        /// <summary>
        /// PAUSE key
        /// </summary>
        VK_PAUSE = 0x13,

        /// <summary>
        /// CAPS LOCK key
        /// </summary>
        VK_CAPITAL = 0x14,

        /// <summary>
        /// IME Kana mode
        /// </summary>
        VK_KANA = 0x15,

        /// <summary>
        /// IME Hangul mode
        /// </summary>
        VK_HANGUL = 0x15,

        /// <summary>
        /// IME On
        /// </summary>
        VK_IME_ON = 0x16,

        /// <summary>
        /// IME Junja mode
        /// </summary>
        VK_JUNJA = 0x17,

        /// <summary>
        /// IME final mode
        /// </summary>
        VK_FINAL = 0x18,

        /// <summary>
        /// IME Hanja mode
        /// </summary>
        VK_HANJA = 0x19,

        /// <summary>
        /// IME Kanji mode
        /// </summary>
        VK_KANJI = 0x19,

        /// <summary>
        /// IME Off
        /// </summary>
        VK_IME_OFF = 0x1A,

        /// <summary>
        /// ESC key
        /// </summary>
        VK_ESCAPE = 0x1B,

        /// <summary>
        /// IME convert
        /// </summary>
        VK_CONVERT = 0x1C,

        /// <summary>
        /// IME nonconvert
        /// </summary>
        VK_NONCONVERT = 0x1D,

        /// <summary>
        /// IME accept
        /// </summary>
        VK_ACCEPT = 0x1E,

        /// <summary>
        /// IME mode change request
        /// </summary>
        VK_MODECHANGE = 0x1F,

        /// <summary>
        /// SPACEBAR
        /// </summary>
        VK_SPACE = 0x20,

        /// <summary>
        /// PAGE UP key
        /// </summary>
        VK_PRIOR = 0x21,

        /// <summary>
        /// PAGE DOWN key
        /// </summary>
        VK_NEXT = 0x22,

        /// <summary>
        /// END key
        /// </summary>
        VK_END = 0x23,

        /// <summary>
        /// HOME key
        /// </summary>
        VK_HOME = 0x24,

        /// <summary>
        /// LEFT ARROW key
        /// </summary>
        VK_LEFT = 0x25,

        /// <summary>
        /// UP ARROW key
        /// </summary>
        VK_UP = 0x26,

        /// <summary>
        /// RIGHT ARROW key
        /// </summary>
        VK_RIGHT = 0x27,

        /// <summary>
        /// DOWN ARROW key
        /// </summary>
        VK_DOWN = 0x28,

        /// <summary>
        /// SELECT key
        /// </summary>
        VK_SELECT = 0x29,

        /// <summary>
        /// PRINT key
        /// </summary>
        VK_PRINT = 0x2A,

        /// <summary>
        /// EXECUTE key
        /// </summary>
        VK_EXECUTE = 0x2B,

        /// <summary>
        /// PRINT SCREEN key
        /// </summary>
        VK_SNAPSHOT = 0x2C,

        /// <summary>
        /// INS key
        /// </summary>
        VK_INSERT = 0x2D,

        /// <summary>
        /// DEL key
        /// </summary>
        VK_DELETE = 0x2E,

        /// <summary>
        /// HELP key
        /// </summary>
        VK_HELP = 0x2F,

        VK_0 = 0x30,
        VK_1 = 0x31,
        VK_2 = 0x32,
        VK_3 = 0x33,
        VK_4 = 0x34,
        VK_5 = 0x35,
        VK_6 = 0x36,
        VK_7 = 0x37,
        VK_8 = 0x38,
        VK_9 = 0x39,
        VK_A = 0x41,    // Alpha-numeric keys, kinda unnecessary to describe
        VK_B = 0x42,
        VK_C = 0x43,
        VK_D = 0x44,
        VK_E = 0x45,
        VK_F = 0x46,
        VK_G = 0x47,
        VK_H = 0x48,
        VK_I = 0x49,
        VK_J = 0x4A,
        VK_K = 0x4B,
        VK_L = 0x4C,
        VK_M = 0x4D,
        VK_N = 0x4E,
        VK_O = 0x4F,
        VK_P = 0x50,
        VK_Q = 0x51,
        VK_R = 0x52,
        VK_S = 0x53,
        VK_T = 0x54,
        VK_U = 0x55,
        VK_V = 0x56,
        VK_W = 0x57,
        VK_X = 0x58,
        VK_Y = 0x59,
        VK_Z = 0x5A,

        /// <summary>
        /// Left Windows key
        /// </summary>
        VK_LWIN = 0x5B,

        /// <summary>
        /// Right Windows key
        /// </summary>
        VK_RWIN = 0x5C,

        /// <summary>
        /// Applications key
        /// </summary>
        VK_APPS = 0x5D,

        /// <summary>
        /// Computer Sleep key
        /// </summary>
        VK_SLEEP = 0x5F,

        VK_NUMPAD0 = 0x60,
        VK_NUMPAD1 = 0x61,
        VK_NUMPAD2 = 0x62,
        VK_NUMPAD3 = 0x63,  // Numpad keys, kinda unnecessary to describe
        VK_NUMPAD4 = 0x64,
        VK_NUMPAD5 = 0x65,
        VK_NUMPAD6 = 0x66,
        VK_NUMPAD7 = 0x67,
        VK_NUMPAD8 = 0x68,
        VK_NUMPAD9 = 0x69,

        /// <summary>
        /// Numpad Multiply key
        /// </summary>
        VK_MULTIPLY = 0x6A,

        /// <summary>
        /// Numpad Add key
        /// </summary>
        VK_ADD = 0x6B,

        /// <summary>
        /// Numpad Separator key
        /// </summary>
        VK_SEPARATOR = 0x6C,

        /// <summary>
        /// Numpad Subtract key
        /// </summary>
        VK_SUBTRACT = 0x6D,

        /// <summary>
        /// Numpad Decimal key
        /// </summary>
        VK_DECIMAL = 0x6E,

        /// <summary>
        /// Numpad Divide key
        /// </summary>
        VK_DIVIDE = 0x6F,

        VK_F1 = 0x70,
        VK_F2 = 0x71,
        VK_F3 = 0x72,
        VK_F4 = 0x73,
        VK_F5 = 0x74,
        VK_F6 = 0x75,
        VK_F7 = 0x76,
        VK_F8 = 0x77,
        VK_F9 = 0x78,
        VK_F10 = 0x79,
        VK_F11 = 0x7A,  // Function keys, kinda unnecessary to describe
        VK_F12 = 0x7B,
        VK_F13 = 0x7C,
        VK_F14 = 0x7D,
        VK_F15 = 0x7E,
        VK_F16 = 0x7F,
        VK_F17 = 0x80,
        VK_F18 = 0x81,
        VK_F19 = 0x82,
        VK_F20 = 0x83,
        VK_F21 = 0x84,
        VK_F22 = 0x85,
        VK_F23 = 0x86,
        VK_F24 = 0x87,

        /// <summary>
        /// NUM LOCK key
        /// </summary>
        VK_NUMLOCK = 0x90,

        /// <summary>
        /// SCROLL LOCK key
        /// </summary>
        VK_SCROLL = 0x91,

        /// <summary>
        /// Left SHIFT key
        /// </summary>
        VK_LSHIFT = 0xA0,

        /// <summary>
        /// Right SHIFT key
        /// </summary>
        VK_RSHIFT = 0xA1,

        /// <summary>
        /// Left CONTROL key
        /// </summary>
        VK_LCONTROL = 0xA2,

        /// <summary>
        /// Right CONTROL key
        /// </summary>
        VK_RCONTROL = 0xA3,

        /// <summary>
        /// Left ALT key
        /// </summary>
        VK_LMENU = 0xA4,

        /// <summary>
        /// Right ALT key
        /// </summary>
        VK_RMENU = 0xA5,

        /// <summary>
        /// Browser Back key
        /// </summary>
        VK_BROWSER_BACK = 0xA6,

        /// <summary>
        /// Browser Forward key
        /// </summary>
        VK_BROWSER_FORWARD = 0xA7,

        /// <summary>
        /// Browser Refresh key
        /// </summary>
        VK_BROWSER_REFRESH = 0xA8,

        /// <summary>
        /// Browser Stop key
        /// </summary>
        VK_BROWSER_STOP = 0xA9,

        /// <summary>
        /// Browser Search key
        /// </summary>
        VK_BROWSER_SEARCH = 0xAA,

        /// <summary>
        /// Browser Favorites key
        /// </summary>
        VK_BROWSER_FAVOTITES = 0xAB,

        /// <summary>
        /// Browser Start and Home key
        /// </summary>
        VK_BROWSER_HOME = 0xAC,

        /// <summary>
        /// Volume Mute key
        /// </summary>
        VK_VOLUME_MUTE = 0xAD,

        /// <summary>
        /// Volume Down key
        /// </summary>
        VK_VOLUME_DOWN = 0xAE,

        /// <summary>
        /// Volume Up key
        /// </summary>
        VK_VOLUME_UP = 0xAF,

        /// <summary>
        /// Next Track key
        /// </summary>
        VK_MEDIA_NEXT_TRACK = 0xB0,

        /// <summary>
        /// Previous Track key
        /// </summary>
        VK_MEDIA_PREV_TRACK = 0xB1,

        /// <summary>
        /// Stop Media key
        /// </summary>
        VK_MEDIA_STOP = 0xB2,

        /// <summary>
        /// Play/Pause Media key
        /// </summary>
        VK_MEDIA_PLAY_PAUSE = 0xB3,

        /// <summary>
        /// Start Mail key
        /// </summary>
        VK_LAUNCH_MAIL = 0xB4,

        /// <summary>
        /// Start Media key
        /// </summary>
        VK_LAUNCH_MEDIA_SELECT = 0xB5,

        /// <summary>
        /// Start Application 1 key
        /// </summary>
        VK_LAUNCH_APP1 = 0xB6,

        /// <summary>
        /// Start Application 2 key
        /// </summary>
        VK_LAUNCH_APP2 = 0xB7,

        /// <summary>
        /// Used for miscellaneous characters; it can vary by keyboard. For the US standard keyboard, the ;: key
        /// </summary>
        VK_OEM_1 = 0xBA,

        /// <summary>
        /// For any country/region, the + key
        /// </summary>
        VK_OEM_PLUS = 0xBB,

        /// <summary>
        /// For any country/region, the , key
        /// </summary>
        VK_OEM_COMMA = 0xBC,

        /// <summary>
        /// For any country/region, the - key
        /// </summary>
        VK_OEM_MINUS = 0xBD,

        /// <summary>
        /// For any country/region, the . key
        /// </summary>
        VK_OEM_PERIOD = 0xBE,

        /// <summary>
        /// Used for miscellaneous characters; it can vary by keyboard. For the US standard keyboard, the /? key
        /// </summary>
        VK_OEM_2 = 0xBF,

        /// <summary>
        /// Used for miscellaneous characters; it can vary by keyboard. For the US standard keyboard, the `~ key
        /// </summary>
        VK_OEM_3 = 0xC0,

        /// <summary>
        /// Used for miscellaneous characters; it can vary by keyboard. For the US standard keyboard, the [{ key
        /// </summary>
        VK_OEM_4 = 0xDB,

        /// <summary>
        /// Used for miscellaneous characters; it can vary by keyboard. For the US standard keyboard, the \\| key
        /// </summary>
        VK_OEM_5 = 0xDC,

        /// <summary>
        /// Used for miscellaneous characters; it can vary by keyboard. For the US standard keyboard, the ]} key
        /// </summary>
        VK_OEM_6 = 0xDD,

        /// <summary>
        /// Used for miscellaneous characters; it can vary by keyboard. For the US standard keyboard, the '\" key
        /// </summary>
        VK_OEM_7 = 0xDE,

        /// <summary>
        /// Used for miscellaneous characters; it can vary by keyboard.
        /// </summary>
        VK_OEM_8 = 0xDF,

        /// <summary>
        /// The <> keys on the US standard keyboard, or the \\| key on the non-US 102-key keyboard
        /// </summary>
        VK_OEM_102 = 0xE2,

        /// <summary>
        /// IME PROCESS key
        /// </summary>
        VK_PROCESSKEY = 0xE5,

        /// <summary>
        /// Used to pass Unicode characters as if they were keystrokes. The VK_PACKET key is the low word of a 32-bit Virtual Key value used for non-keyboard input methods. For more information, see Remark in KEYBDINPUT, SendInput, WM_KEYDOWN, and WM_KEYUP
        /// </summary>
        VK_PACKET = 0xE7,

        /// <summary>
        /// Attn key
        /// </summary>
        VK_ATTN = 0xF6,

        /// <summary>
        /// CrSel key
        /// </summary>
        VK_CRSEL = 0xF7,

        /// <summary>
        /// ExSel key
        /// </summary>
        VK_EXSEL = 0xF8,

        /// <summary>
        /// Erase EOF key
        /// </summary>
        VK_EREOF = 0xF9,

        /// <summary>
        /// Play key
        /// </summary>
        VK_PLAY = 0xFA,

        /// <summary>
        /// Zoom key
        /// </summary>
        VK_ZOOM = 0xFB,

        /// <summary>
        /// Reserved
        /// </summary>
        VK_NONAME = 0xFC,

        /// <summary>
        /// PA1 key
        /// </summary>
        VK_PA1 = 0xFD,

        /// <summary>
        /// Clear key
        /// </summary>
        VK_OEM_CLEAR = 0xFE
    }
    #endregion

    #region Mouse
    [StructLayout( LayoutKind.Sequential )]
    public struct MOUSEINPUT
    {
        internal int dx;
        internal int dy;
        internal int mouseData;
        internal MOUSEEVENTF dwFlags;
        internal uint time;
        internal UIntPtr dwExtraInfo;
    }

    [Flags]
    public enum MOUSEEVENTF : uint
    {
        ABSOLUTE = 0x8000,
        HWHEEL = 0x01000,
        MOVE = 0x0001,
        MOVE_NOCOALESCE = 0x2000,
        LEFTDOWN = 0x0002,
        LEFTUP = 0x0004,
        RIGHTDOWN = 0x0008,
        RIGHTUP = 0x0010,
        MIDDLEDOWN = 0x0020,
        MIDDLEUP = 0x0040,
        VIRTUALDESK = 0x4000,
        WHEEL = 0x0800,
        XDOWN = 0x0080,
        XUP = 0x0100
    }
    #endregion
    #endregion
}
#endregion