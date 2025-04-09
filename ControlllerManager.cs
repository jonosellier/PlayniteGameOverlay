using SDL2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Timers;

namespace PlayniteGameOverlay
{
    public class ControllerManager : IDisposable
    {
        // Singleton instance
        private static ControllerManager _instance;
        public static ControllerManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new InvalidOperationException("ControllerManager not initialized. Call Initialize() first.");
                }
                return _instance;
            }
        }

        // Create and initialize the singleton
        public static ControllerManager Initialize(bool debugMode)
        {
            if (_instance == null)
            {
                _instance = new ControllerManager(debugMode);
                if (_instance.InitializeSDL())
                {
                    _instance.StartPolling();
                }
            }
            return _instance;
        }

        private readonly Logger _logger;
        private IntPtr _controller = IntPtr.Zero;
        private bool _sdlInitialized = false;
        private int _controllerId = -1;
        private System.Threading.Timer _pollingTimer;

        // For tracking button state changes
        private ControllerState _previousState = new ControllerState();
        private Dictionary<string, Stopwatch> _buttonHoldTimers = new Dictionary<string, Stopwatch>();
        private Dictionary<string, bool> _repeatFired = new Dictionary<string, bool>();

        public const short DEADZONE = 10000;
        public const int DEBOUNCE_THRESHOLD = 200; // milliseconds
        public const int REPEAT_DELAY = 250; // milliseconds for repeating events when held
        public const int POLLING_INTERVAL = 16; // ~60Hz in milliseconds

        // Event for button/control actions
        public event EventHandler<ControllerEventArgs> ControllerAction;

        // Private constructor enforces the singleton pattern
        private ControllerManager(bool debugMode)
        {
            _logger = new Logger(debugMode);
        }

        private bool InitializeSDL()
        {
            try
            {
                _logger.Log("Initializing SDL controller support", "SDL");

                // Initialize SDL with game controller support
                if (SDL.SDL_Init(SDL.SDL_INIT_GAMECONTROLLER) < 0)
                {
                    string error = SDL.SDL_GetError();
                    _logger.Log($"SDL could not initialize! SDL Error: {error}", "SDL_ERROR");
                    return false;
                }

                _sdlInitialized = true;
                _logger.Log("SDL initialized successfully", "SDL");

                // Look for connected controllers
                int numJoysticks = SDL.SDL_NumJoysticks();
                _logger.Log($"Found {numJoysticks} joysticks/controllers", "SDL");

                // Try to find a connected compatible controller
                for (int i = 0; i < numJoysticks; i++)
                {
                    if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_TRUE)
                    {
                        _controllerId = i;
                        _logger.Log($"Found compatible game controller at index {i}", "SDL");

                        // Open the controller here, and keep it open
                        _controller = SDL.SDL_GameControllerOpen(_controllerId);
                        if (_controller == IntPtr.Zero)
                        {
                            _logger.Log($"Could not open controller! SDL Error: {SDL.SDL_GetError()}", "SDL_ERROR");
                            return false;
                        }

                        // Optional: Log controller mapping
                        string mapping = SDL.SDL_GameControllerMapping(_controller);
                        _logger.Log($"Controller mapping: {mapping}", "SDL_DEBUG");

                        break;
                    }
                }

                if (_controllerId == -1)
                {
                    _logger.Log("No compatible game controllers found", "SDL");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.Log($"Error initializing SDL: {ex.Message}", "SDL_ERROR");
                _logger.Log($"Stack trace: {ex.StackTrace}", "SDL_ERROR");
                return false;
            }
        }

        private void StartPolling()
        {
            _logger.Log("Starting controller polling at 60Hz", "SDL");
            _pollingTimer = new System.Threading.Timer(
                _ => Update(),
                null,
                0,
                POLLING_INTERVAL);
        }

        private void Update()
        {
            var currentState = PollInput();
            ProcessStateChanges(currentState);
            _previousState = currentState;
        }

        private void ProcessStateChanges(ControllerState currentState)
        {
            // Check A button
            ProcessButton("A", _previousState.APressed, currentState.APressed);

            // Check B button
            ProcessButton("B", _previousState.BPressed, currentState.BPressed);

            // Check directional inputs
            ProcessButton("Up", _previousState.MoveUp, currentState.MoveUp);
            ProcessButton("Down", _previousState.MoveDown, currentState.MoveDown);
            ProcessButton("Left", _previousState.MoveLeft, currentState.MoveLeft);
            ProcessButton("Right", _previousState.MoveRight, currentState.MoveRight);

            // Check menu buttons
            ProcessButton("Start", _previousState.StartPressed, currentState.StartPressed);
            ProcessButton("Back", _previousState.BackPressed, currentState.BackPressed);
            ProcessButton("Guide", _previousState.GuidePressed, currentState.GuidePressed);
        }

        private void ProcessButton(string buttonName, bool wasPressed, bool isPressed)
        {
            // Initialize timers if they don't exist
            if (!_buttonHoldTimers.ContainsKey(buttonName))
            {
                _buttonHoldTimers[buttonName] = new Stopwatch();
                _repeatFired[buttonName] = false;
            }

            // Button was just pressed
            if (!wasPressed && isPressed)
            {
                _buttonHoldTimers[buttonName].Restart();
                _repeatFired[buttonName] = false;
                FireControllerEvent(buttonName, ControllerEventType.Pressed);
            }
            // Button is being held
            else if (wasPressed && isPressed)
            {
                if (_buttonHoldTimers[buttonName].ElapsedMilliseconds > REPEAT_DELAY)
                {
                    FireControllerEvent(buttonName, ControllerEventType.Repeated);
                    _buttonHoldTimers[buttonName].Restart();
                }
            }
            // Button was just released
            else if (wasPressed && !isPressed)
            {
                _buttonHoldTimers[buttonName].Stop();
                FireControllerEvent(buttonName, ControllerEventType.Released);
            }
        }

        private void FireControllerEvent(string buttonName, ControllerEventType eventType)
        {
            ControllerAction?.Invoke(this, new ControllerEventArgs(buttonName, eventType));
            _logger.Log($"Controller {eventType}: {buttonName}", "SDL_EVENT");
        }

        public ControllerState PollInput()
        {
            var state = new ControllerState();

            // Ensure that the controller is initialized and opened
            if (_controller == IntPtr.Zero)
            {
                return state;
            }

            // Process SDL events
            SDL.SDL_Event sdlEvent;
            while (SDL.SDL_PollEvent(out sdlEvent) != 0)
            {
                if (sdlEvent.type == SDL.SDL_EventType.SDL_CONTROLLERDEVICEADDED)
                {
                    _logger.Log($"Controller device added: {sdlEvent.cdevice.which}", "SDL_EVENT");
                }
                else if (sdlEvent.type == SDL.SDL_EventType.SDL_CONTROLLERDEVICEREMOVED)
                {
                    _logger.Log($"Controller device removed: {sdlEvent.cdevice.which}", "SDL_EVENT");
                }
            }

            // Update the controller's state
            SDL.SDL_GameControllerUpdate();

            // Check button states
            state.APressed = SDL.SDL_GameControllerGetButton(_controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A) == 1;
            state.BPressed = SDL.SDL_GameControllerGetButton(_controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B) == 1;

            // Check D-Pad buttons
            bool dpadUp = SDL.SDL_GameControllerGetButton(_controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP) == 1;
            bool dpadDown = SDL.SDL_GameControllerGetButton(_controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN) == 1;
            bool dpadLeft = SDL.SDL_GameControllerGetButton(_controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT) == 1;
            bool dpadRight = SDL.SDL_GameControllerGetButton(_controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT) == 1;

            // Check analog stick values
            short leftX = SDL.SDL_GameControllerGetAxis(_controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX);
            short leftY = SDL.SDL_GameControllerGetAxis(_controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY);

            // Combine D-Pad and Left Stick for movement detection
            state.MoveUp = dpadUp || leftY < -DEADZONE;
            state.MoveDown = dpadDown || leftY > DEADZONE;
            state.MoveLeft = dpadLeft || leftX < -DEADZONE;
            state.MoveRight = dpadRight || leftX > DEADZONE;
            state.StartPressed = SDL.SDL_GameControllerGetButton(_controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START) == 1;
            state.BackPressed = SDL.SDL_GameControllerGetButton(_controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK) == 1;
            state.GuidePressed = SDL.SDL_GameControllerGetButton(_controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE) == 1;

            return state;
        }

        public void Close()
        {
            if (_pollingTimer != null)
            {
                _pollingTimer.Dispose();
                _pollingTimer = null;
            }

            if (_controller != IntPtr.Zero)
            {
                SDL.SDL_GameControllerClose(_controller);
                _controller = IntPtr.Zero;
                _logger.Log("Controller closed", "SDL");
            }

            if (_sdlInitialized)
            {
                SDL.SDL_Quit();
                _sdlInitialized = false;
            }
        }

        public void Dispose()
        {
            Close();
            _instance = null;
        }
    }

    public enum ControllerEventType
    {
        Pressed,
        Repeated,
        Released
    }

    public class ControllerEventArgs : EventArgs
    {
        public string ButtonName { get; }
        public ControllerEventType EventType { get; }

        public ControllerEventArgs(string buttonName, ControllerEventType eventType)
        {
            ButtonName = buttonName;
            EventType = eventType;
        }
    }

    public class ControllerState
    {
        public bool APressed { get; set; }
        public bool BPressed { get; set; }
        public bool StartPressed { get; set; }
        public bool BackPressed { get; set; }
        public bool GuidePressed { get; set; }
        public bool MoveUp { get; set; }
        public bool MoveDown { get; set; }
        public bool MoveLeft { get; set; }
        public bool MoveRight { get; set; }
    }
}