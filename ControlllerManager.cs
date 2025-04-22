using SDL2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

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
        private bool _sdlInitialized = false;
        private Timer _pollingTimer;

        // For tracking controllers and their states
        private class ControllerData
        {
            public int ControllerId { get; set; }
            public IntPtr ControllerHandle { get; set; }
            public ControllerState PreviousState { get; set; } = new ControllerState();
            public Dictionary<string, Stopwatch> ButtonHoldTimers { get; set; } = new Dictionary<string, Stopwatch>();
            public Dictionary<string, bool> RepeatFired { get; set; } = new Dictionary<string, bool>();
        }

        private List<ControllerData> _controllers = new List<ControllerData>();

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
                SDL.SDL_SetHint(SDL.SDL_HINT_XINPUT_ENABLED, "0");
                // Initialize SDL with game controller support
                if (SDL.SDL_Init(SDL.SDL_INIT_GAMECONTROLLER | SDL.SDL_INIT_JOYSTICK | SDL.SDL_INIT_EVENTS) < 0)
                {
                    string error = SDL.SDL_GetError();
                    _logger.Log($"SDL could not initialize! SDL Error: {error}", "SDL_ERROR");
                    return false;
                }

                int mappingsAdded = SDL.SDL_GameControllerAddMappingsFromFile("gamecontrollerdb.txt");
                if (mappingsAdded == -1)
                {
                    _logger.Log("Failed to load game controller mappings", "SDL_DB");
                    _logger.Log(SDL.SDL_GetError(), "SDL_DB");
                }
                else
                {
                    _logger.Log($"Loaded {mappingsAdded} controller mapping(s)", "SDL_DB");
                }

                _sdlInitialized = true;
                _logger.Log("SDL initialized successfully", "SDL");

                // Scan and add all connected controllers
                ScanForControllers();

                return _controllers.Count > 0;
            }
            catch (Exception ex)
            {
                _logger.Log($"Error initializing SDL: {ex.Message}", "SDL_ERROR");
                _logger.Log($"Stack trace: {ex.StackTrace}", "SDL_ERROR");
                return false;
            }
        }

        public void ScanForControllers()
        {
            // Look for connected controllers
            int numJoysticks = SDL.SDL_NumJoysticks();
            _logger.Log($"Found {numJoysticks} joysticks/controllers", "SDL");

            // Try to find all connected compatible controllers
            for (int i = 0; i < numJoysticks; i++)
            {
                // Skip if we already have this controller
                if (_controllers.Exists(c => c.ControllerId == i))
                    continue;

                if (SDL.SDL_IsGameController(i) == SDL.SDL_bool.SDL_TRUE)
                {
                    _logger.Log($"Found compatible game controller at index {i}", "SDL");

                    // Open the controller
                    IntPtr controllerHandle = SDL.SDL_GameControllerOpen(i);
                    if (controllerHandle == IntPtr.Zero)
                    {
                        _logger.Log($"Could not open controller {i}! SDL Error: {SDL.SDL_GetError()}", "SDL_ERROR");
                        continue;
                    }

                    // Optional: Log controller mapping
                    string mapping = SDL.SDL_GameControllerMapping(controllerHandle);
                    _logger.Log($"Controller {i} mapping: {mapping}", "SDL_DEBUG");

                    // Add this controller to our list
                    _controllers.Add(new ControllerData
                    {
                        ControllerId = i,
                        ControllerHandle = controllerHandle
                    });
                }
            }

            _logger.Log($"Successfully connected to {_controllers.Count} controllers", "SDL");
        }

        private void StartPolling()
        {
            _logger.Log("Starting controller polling at 60Hz", "SDL");
            _pollingTimer = new Timer(
                _ => Update(),
                null,
                0,
                POLLING_INTERVAL);
        }

        private void Update()
        {
            // Process SDL events first (handle controller connections/disconnections)
            ProcessSDLEvents();

            // Update each connected controller
            try
            {
                foreach (var controller in _controllers)
                {
                    if (controller.ControllerHandle != IntPtr.Zero)
                    {
                        var currentState = PollInput(controller.ControllerHandle);
                        ProcessStateChanges(controller, currentState);
                        controller.PreviousState = currentState;
                    }
                }
            }
            catch { 
            }
        }

        private void ProcessSDLEvents()
        {
            // Process SDL events to detect controller connections/disconnections
            SDL.SDL_Event sdlEvent;
            while (SDL.SDL_PollEvent(out sdlEvent) != 0)
            {
                if (sdlEvent.type == SDL.SDL_EventType.SDL_CONTROLLERDEVICEADDED)
                {
                    int deviceIndex = sdlEvent.cdevice.which;
                    _logger.Log($"Controller device added: {deviceIndex}", "SDL_EVENT");
                    ScanForControllers();
                }
                else if (sdlEvent.type == SDL.SDL_EventType.SDL_CONTROLLERDEVICEREMOVED)
                {
                    int instanceId = sdlEvent.cdevice.which;
                    _logger.Log($"Controller device removed: {instanceId}", "SDL_EVENT");

                    // Find and remove the disconnected controller
                    for (int i = _controllers.Count - 1; i >= 0; i--)
                    {
                        if (_controllers[i].ControllerHandle != IntPtr.Zero)
                        {
                            int controllerInstanceId = SDL.SDL_JoystickInstanceID(
                                SDL.SDL_GameControllerGetJoystick(_controllers[i].ControllerHandle));

                            if (controllerInstanceId == instanceId)
                            {
                                SDL.SDL_GameControllerClose(_controllers[i].ControllerHandle);
                                _controllers.RemoveAt(i);
                                _logger.Log($"Removed disconnected controller instance: {instanceId}", "SDL");
                                break;
                            }
                        }
                    }
                }
            }
        }

        private void ProcessStateChanges(ControllerData controller, ControllerState currentState)
        {
            int controllerId = controller.ControllerId;

            // Face buttons
            ProcessButton(controller, "A", controller.PreviousState.APressed, currentState.APressed, controllerId);
            ProcessButton(controller, "B", controller.PreviousState.BPressed, currentState.BPressed, controllerId);
            ProcessButton(controller, "X", controller.PreviousState.XPressed, currentState.XPressed, controllerId);
            ProcessButton(controller, "Y", controller.PreviousState.YPressed, currentState.YPressed, controllerId);

            // Shoulder buttons
            ProcessButton(controller, "LeftShoulder", controller.PreviousState.LeftShoulderPressed, currentState.LeftShoulderPressed, controllerId);
            ProcessButton(controller, "RightShoulder", controller.PreviousState.RightShoulderPressed, currentState.RightShoulderPressed, controllerId);

            // Triggers as digital buttons
            ProcessButton(controller, "LeftTrigger", controller.PreviousState.LeftTriggerPressed, currentState.LeftTriggerPressed, controllerId);
            ProcessButton(controller, "RightTrigger", controller.PreviousState.RightTriggerPressed, currentState.RightTriggerPressed, controllerId);

            // Stick clicks
            ProcessButton(controller, "LeftStick", controller.PreviousState.LeftStickPressed, currentState.LeftStickPressed, controllerId);
            ProcessButton(controller, "RightStick", controller.PreviousState.RightStickPressed, currentState.RightStickPressed, controllerId);

            // Menu buttons
            ProcessButton(controller, "Start", controller.PreviousState.StartPressed, currentState.StartPressed, controllerId);
            ProcessButton(controller, "Back", controller.PreviousState.BackPressed, currentState.BackPressed, controllerId);
            ProcessButton(controller, "Guide", controller.PreviousState.GuidePressed, currentState.GuidePressed, controllerId);

            // Primary directional inputs (left stick + d-pad)
            ProcessButton(controller, "Up", controller.PreviousState.MoveUp, currentState.MoveUp, controllerId);
            ProcessButton(controller, "Down", controller.PreviousState.MoveDown, currentState.MoveDown, controllerId);
            ProcessButton(controller, "Left", controller.PreviousState.MoveLeft, currentState.MoveLeft, controllerId);
            ProcessButton(controller, "Right", controller.PreviousState.MoveRight, currentState.MoveRight, controllerId);

            // Secondary directional inputs (right stick)
            ProcessButton(controller, "UpAlt", controller.PreviousState.MoveUpAlt, currentState.MoveUpAlt, controllerId);
            ProcessButton(controller, "DownAlt", controller.PreviousState.MoveDownAlt, currentState.MoveDownAlt, controllerId);
            ProcessButton(controller, "LeftAlt", controller.PreviousState.MoveLeftAlt, currentState.MoveLeftAlt, controllerId);
            ProcessButton(controller, "RightAlt", controller.PreviousState.MoveRightAlt, currentState.MoveRightAlt, controllerId);

            // Modern controller additional buttons
            ProcessButton(controller, "Touchpad", controller.PreviousState.TouchpadPressed, currentState.TouchpadPressed, controllerId);
            ProcessButton(controller, "Share", controller.PreviousState.SharePressed, currentState.SharePressed, controllerId);
            ProcessButton(controller, "Misc", controller.PreviousState.MiscPressed, currentState.MiscPressed, controllerId);

            // Process any custom buttons stored in the dictionary
            if (currentState is IEnumerable<KeyValuePair<string, bool>> customButtons)
            {
                foreach (var pair in customButtons)
                {
                    bool previousValue = false;
                    if (controller.PreviousState is IEnumerable<KeyValuePair<string, bool>> prevCustomButtons)
                    {
                        // Try to get the previous value if it exists
                        foreach (var prevPair in prevCustomButtons)
                        {
                            if (prevPair.Key == pair.Key)
                            {
                                previousValue = prevPair.Value;
                                break;
                            }
                        }
                    }

                    ProcessButton(controller, pair.Key, previousValue, pair.Value, controllerId);
                }
            }
        }

        private void ProcessButton(ControllerData controller, string buttonName, bool wasPressed, bool isPressed, int controllerId)
        {
            // Initialize timers if they don't exist
            string buttonKey = buttonName;
            if (!controller.ButtonHoldTimers.ContainsKey(buttonKey))
            {
                try
                {
                    controller.ButtonHoldTimers[buttonKey] = new Stopwatch();
                    controller.RepeatFired[buttonKey] = false;
                }
                catch (Exception ex)
                {
                    _logger.Log($"Error initializing button timers for {buttonKey}: {ex.Message}", "SDL_ERROR");
                }
            }

            // Button was just pressed
            if (!wasPressed && isPressed)
            {
                controller.ButtonHoldTimers[buttonKey].Restart();
                controller.RepeatFired[buttonKey] = false;
                FireControllerEvent(buttonName, ControllerEventType.Pressed, controllerId);
            }
            // Button is being held
            else if (wasPressed && isPressed)
            {
                if (controller.ButtonHoldTimers[buttonKey].ElapsedMilliseconds > REPEAT_DELAY)
                {
                    FireControllerEvent(buttonName, ControllerEventType.Repeated, controllerId);
                    controller.ButtonHoldTimers[buttonKey].Restart();
                }
            }
            // Button was just released
            else if (wasPressed && !isPressed)
            {
                controller.ButtonHoldTimers[buttonKey].Stop();
                FireControllerEvent(buttonName, ControllerEventType.Released, controllerId);
            }
        }

        private void FireControllerEvent(string buttonName, ControllerEventType eventType, int controllerId)
        {
            ControllerAction?.Invoke(this, new ControllerEventArgs(buttonName, eventType, controllerId));
            _logger.Log($"Controller {controllerId} {eventType}: {buttonName}", "SDL_EVENT");
        }

        public ControllerState PollInput(IntPtr controller)
        {
            var state = new ControllerState();

            // Ensure that the controller is initialized and opened
            if (controller == IntPtr.Zero)
            {
                return state;
            }

            // Update the controller's state
            SDL.SDL_GameControllerUpdate();

            // Process any pending events to ensure we don't miss guide button presses
            SDL.SDL_Event sdlEvent;
            while (SDL.SDL_PollEvent(out sdlEvent) != 0)
            {
                // Don't let these events accumulate in the main processing loop
                // Just process them here to keep input state fresh
            }

            // Check face buttons
            state.APressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_A) == 1;
            state.BPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_B) == 1;
            state.XPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_X) == 1;
            state.YPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_Y) == 1;

            // Check shoulder buttons
            state.LeftShoulderPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSHOULDER) == 1;
            state.RightShoulderPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSHOULDER) == 1;

            // Handle digital triggers (treat as pressed if over half the range)
            short leftTriggerValue = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERLEFT);
            short rightTriggerValue = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_TRIGGERRIGHT);
            state.LeftTriggerPressed = leftTriggerValue > 16384; // Half of max value (32767)
            state.RightTriggerPressed = rightTriggerValue > 16384;

            // Check menu buttons
            state.StartPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_START) == 1;
            state.BackPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_BACK) == 1;

            // Special handling for the guide button
            try
            {
                // Try multiple ways to get the guide button state
                bool guidePressed = false;

                // Standard way
                guidePressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_GUIDE) == 1;

                // If that fails, try with the numerical value directly
                if (!guidePressed)
                {
                    guidePressed = SDL.SDL_GameControllerGetButton(controller, (SDL.SDL_GameControllerButton)10) == 1;
                }

                // If that still fails, try yet another approach with joystick button mapping
                if (!guidePressed && SDL.SDL_GameControllerGetAttached(controller) == SDL.SDL_bool.SDL_TRUE)
                {
                    IntPtr joystick = SDL.SDL_GameControllerGetJoystick(controller);
                    if (joystick != IntPtr.Zero)
                    {
                        // The guide button is often mapped to button 10 on Xbox controllers
                        guidePressed = SDL.SDL_JoystickGetButton(joystick, 10) == 1;
                    }
                }

                state.GuidePressed = guidePressed;
            }
            catch
            {
                // Fallback if any exceptions occur
                state.GuidePressed = false;
            }

            // Check thumbstick buttons
            state.LeftStickPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_LEFTSTICK) == 1;
            state.RightStickPressed = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_RIGHTSTICK) == 1;

            // Check D-Pad buttons
            bool dpadUp = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_UP) == 1;
            bool dpadDown = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_DOWN) == 1;
            bool dpadLeft = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_LEFT) == 1;
            bool dpadRight = SDL.SDL_GameControllerGetButton(controller, SDL.SDL_GameControllerButton.SDL_CONTROLLER_BUTTON_DPAD_RIGHT) == 1;

            // Check stick directions (as digital inputs)
            short leftX = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTX);
            short leftY = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_LEFTY);
            short rightX = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTX);
            short rightY = SDL.SDL_GameControllerGetAxis(controller, SDL.SDL_GameControllerAxis.SDL_CONTROLLER_AXIS_RIGHTY);

            // Primary movement (D-Pad + Left Stick)
            state.MoveUp = dpadUp || leftY < -DEADZONE;
            state.MoveDown = dpadDown || leftY > DEADZONE;
            state.MoveLeft = dpadLeft || leftX < -DEADZONE;
            state.MoveRight = dpadRight || leftX > DEADZONE;

            // Secondary movement (Right Stick as alternate D-Pad)
            state.MoveUpAlt = rightY < -DEADZONE;
            state.MoveDownAlt = rightY > DEADZONE;
            state.MoveLeftAlt = rightX < -DEADZONE;
            state.MoveRightAlt = rightX > DEADZONE;

            // Modern controller additional buttons - try/catch as before
            try
            {
                state.TouchpadPressed = SDL.SDL_GameControllerGetButton(controller, (SDL.SDL_GameControllerButton)15) == 1;
                state.SharePressed = SDL.SDL_GameControllerGetButton(controller, (SDL.SDL_GameControllerButton)16) == 1;
                state.MiscPressed = SDL.SDL_GameControllerGetButton(controller, (SDL.SDL_GameControllerButton)17) == 1;
            }
            catch
            {
                // Ignore exceptions for buttons that may not exist on all controllers
            }

            return state;
        }

        public void Close()
        {
            if (_pollingTimer != null)
            {
                _pollingTimer.Dispose();
                _pollingTimer = null;
            }

            // Close all open controllers
            foreach (var controller in _controllers)
            {
                if (controller.ControllerHandle != IntPtr.Zero)
                {
                    SDL.SDL_GameControllerClose(controller.ControllerHandle);
                }
            }
            _controllers.Clear();

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
        public int ControllerId { get; }

        public ControllerEventArgs(string buttonName, ControllerEventType eventType, int controllerId)
        {
            ButtonName = buttonName;
            EventType = eventType;
            ControllerId = controllerId;
        }
    }

    public class ControllerState
    {
        // Face buttons
        public bool APressed { get; set; }
        public bool BPressed { get; set; }
        public bool XPressed { get; set; }
        public bool YPressed { get; set; }

        // Shoulder buttons
        public bool LeftShoulderPressed { get; set; }
        public bool RightShoulderPressed { get; set; }
        public bool LeftTriggerPressed { get; set; }  // Digital representation of analog trigger
        public bool RightTriggerPressed { get; set; } // Digital representation of analog trigger

        // Menu buttons
        public bool StartPressed { get; set; }
        public bool BackPressed { get; set; }
        public bool GuidePressed { get; set; }

        // Thumbstick buttons
        public bool LeftStickPressed { get; set; }
        public bool RightStickPressed { get; set; }

        // Primary movement (Left stick and D-pad)
        public bool MoveUp { get; set; }
        public bool MoveDown { get; set; }
        public bool MoveLeft { get; set; }
        public bool MoveRight { get; set; }

        // Secondary movement (Right stick)
        public bool MoveUpAlt { get; set; }
        public bool MoveDownAlt { get; set; }
        public bool MoveLeftAlt { get; set; }
        public bool MoveRightAlt { get; set; }

        // Additional buttons (for newer controllers like DualShock/DualSense)
        public bool TouchpadPressed { get; set; }
        public bool SharePressed { get; set; }
        public bool MiscPressed { get; set; }  // For platform-specific additional buttons

        // Dictionary for custom mapping and future compatibility
        private Dictionary<string, bool> _buttonStates = new Dictionary<string, bool>();

        // Indexer for accessing button states by name
        public bool this[string buttonName]
        {
            get => _buttonStates.TryGetValue(buttonName, out bool value) ? value : false;
            set => _buttonStates[buttonName] = value;
        }

        // Helper method to check if any button is pressed
        public bool IsAnyButtonPressed()
        {
            return APressed || BPressed || XPressed || YPressed ||
                   LeftShoulderPressed || RightShoulderPressed ||
                   LeftTriggerPressed || RightTriggerPressed ||
                   StartPressed || BackPressed || GuidePressed ||
                   LeftStickPressed || RightStickPressed ||
                   MoveUp || MoveDown || MoveLeft || MoveRight ||
                   MoveUpAlt || MoveDownAlt || MoveLeftAlt || MoveRightAlt ||
                   TouchpadPressed || SharePressed || MiscPressed ||
                   _buttonStates.Values.Any(v => v);
        }
    }
}