using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

struct UserInput
{
    public bool up;
    public bool down;
    public bool right;
    public bool left;
    public bool jump;

    private string FormatBoolean(bool value)
    {
        return value.ToString().ToLower();
    }

    public override string ToString()
    {
        return $"up={FormatBoolean(up)}, " +
            $"down={FormatBoolean(down)}, " +
            $"right={FormatBoolean(right)}, " +
            $"left={FormatBoolean(left)}, " +
            $"jump={FormatBoolean(jump)}";
    }
}

struct ClientState
{
    public Vector3 position;
    public Quaternion rotation;

    public override string ToString()
    {
        return $"position={position}, " +
            $"rotation={rotation}";
    }
}

struct InputMessage
{
    public float deliveryTime;
    public int startTick;
    public List<UserInput> inputs;

    public override string ToString()
    {
        return $"deliveryTime={deliveryTime}, " +
            $"startTick={startTick}, " +
            $"inputs=({string.Join("),(", inputs)})";
    }
}

struct StateMessage
{
    public float deliveryTime;
    public int tick;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 velocity;
    public Vector3 angularVelocity;

    public override string ToString()
    {
        return $"deliveryTime={deliveryTime}, " +
            $"tick={tick}, " +
            $"position={position}, " +
            $"rotation={rotation}, " +
            $"velocity={velocity}, " +
            $"angularVelocity={angularVelocity}";
    }
}

public class Simulation : MonoBehaviour
{
    public const float MOVE_FLOAT = 0.5f;
    public const float JUMP_THRESHOLD = 0.75f;

    public GameObject clientPlayer;
    public GameObject serverPlayer;
    public bool errorCorrection = true;
    public bool correctionSmoothing = true;
    public bool redundantInput = true;
    public float networkLatency = 0.1f;
    public float packetLoss = 0.05f;
    public uint snapshotRate = 0;

    private PhysicsScene clientScene;
    private float currentTime;
    private int currentTick;
    private int latestTick;
    private const int BUFFER_SIZE = 1024;
    private UserInput[] inputBuffer; //predicted inputs
    private ClientState[] stateBuffer; //predicted states
    private Queue<StateMessage> stateMessages;
    private Vector3 positionError;
    private Quaternion rotationError;

    private PhysicsScene serverScene;
    private int serverTick;
    private Queue<InputMessage> inputMessages;

    void Start()
    {
        currentTime = 0.0f;
        currentTick = 0;
        latestTick = 0;
        inputBuffer = new UserInput[BUFFER_SIZE];
        stateBuffer = new ClientState[BUFFER_SIZE];
        stateMessages = new Queue<StateMessage>();
        positionError = Vector3.zero;
        rotationError = Quaternion.identity;

        inputMessages = new Queue<InputMessage>();

        Scene clientScene = SceneManager.LoadScene("Background",
            new LoadSceneParameters()
            {
                loadSceneMode = LoadSceneMode.Additive,
                localPhysicsMode = LocalPhysicsMode.Physics3D
            });

        SceneManager.MoveGameObjectToScene(clientPlayer, clientScene);
        this.clientScene = clientScene.GetPhysicsScene();

        Scene serverScene = SceneManager.LoadScene("Background",
            new LoadSceneParameters()
            {
                loadSceneMode = LoadSceneMode.Additive,
                localPhysicsMode = LocalPhysicsMode.Physics3D
            });

        SceneManager.MoveGameObjectToScene(serverPlayer, serverScene);
        this.serverScene = serverScene.GetPhysicsScene();
    }

    void Update()
    {
        float deltaTime = Time.fixedDeltaTime;

        currentTime += Time.deltaTime;
        while (currentTime >= deltaTime)
        {
            currentTime -= deltaTime;

            UserInput input = new()
            {
                up = Input.GetKey(KeyCode.W),
                down = Input.GetKey(KeyCode.S),
                right = Input.GetKey(KeyCode.D),
                left = Input.GetKey(KeyCode.A),
                jump = Input.GetKey(KeyCode.Space)
            };

            int index = currentTick % BUFFER_SIZE;
            inputBuffer[index] = input;
            stateBuffer[index] = new()
            {
                position = clientPlayer.transform.position,
                rotation = clientPlayer.transform.rotation
            };

            ApplyForce(clientPlayer, input);
             clientScene.Simulate(deltaTime);

            SendInput();

            currentTick++;
        }

        UpdateClient(deltaTime);
        UpdateServer(deltaTime);
    }

    private void ApplyForce(GameObject player, UserInput input)
    {
        Rigidbody rigidbody = player.GetComponent<Rigidbody>();

        if (input.up)
        {
            rigidbody.AddForce(
                Camera.main.transform.forward * MOVE_FLOAT,
                ForceMode.Impulse);
        }

        if (input.down)
        {
            rigidbody.AddForce(
                -Camera.main.transform.forward * MOVE_FLOAT,
                ForceMode.Impulse);
        }

        if (input.right)
        {
            rigidbody.AddForce(
                Camera.main.transform.right * MOVE_FLOAT,
                ForceMode.Impulse);
        }

        if (input.left)
        {
            rigidbody.AddForce(
                -Camera.main.transform.right * MOVE_FLOAT,
                ForceMode.Impulse);
        }

        if (input.jump && player.transform.position.y <= JUMP_THRESHOLD)
        {
            rigidbody.AddForce(
                Camera.main.transform.up * MOVE_FLOAT,
                ForceMode.Impulse);
        }
    }

    private void SendInput()
    {
        if (Random.value > packetLoss)
        {
            int startTick = redundantInput ? latestTick : currentTick;

            InputMessage message = new()
            {
                deliveryTime = Time.time + networkLatency,
                startTick = startTick,
                inputs = new List<UserInput>()
            };

            for (int i = startTick; i <= currentTick; i++)
            {
                message.inputs.Add(inputBuffer[i % BUFFER_SIZE]);
            }

            Debug.Log($"inputMessage={message}");
            inputMessages.Enqueue(message);
        }
    }

    private string FormatTransform(Transform transform)
    {
        return $"position={transform.position}, " +
            $"rotation={transform.rotation}";
    }

    private bool HasInputMessage()
    {
        return inputMessages.Count > 0 &&
            Time.time >= inputMessages.Peek().deliveryTime;
    }

    private void UpdateServer(float deltaTime)
    {
        Rigidbody rigidbody = serverPlayer.GetComponent<Rigidbody>();

        while (HasInputMessage())
        {
            InputMessage inputMessage = inputMessages.Dequeue();

            int startTick = inputMessage.startTick;
            int inputCount = inputMessage.inputs.Count;
            int maxTick = startTick + inputCount - 1;
            if (maxTick >= serverTick)
            {
                int offsetTick =
                    serverTick > startTick ?
                    serverTick - startTick :
                    0;

                for (int i = offsetTick; i < inputCount; i++)
                {
                    ApplyForce(serverPlayer, inputMessage.inputs[i]);
                    serverScene.Simulate(deltaTime);

                    serverTick++;
                    //++serverTickAccumulator;
                    //if (serverTickAccumulator >= snapshotRate)
                    //{
                    //serverTickAccumulator = 0;

                    if (Random.value > packetLoss)
                    {
                        StateMessage stateMessage = new()
                        {
                            deliveryTime = Time.time + networkLatency,
                            tick = serverTick,
                            position = rigidbody.position,
                            rotation = rigidbody.rotation,
                            velocity = rigidbody.velocity,
                            angularVelocity = rigidbody.angularVelocity
                        };

                        Debug.Log($"stateMessage={stateMessage}");
                        stateMessages.Enqueue(stateMessage);
                    }
                }
            }
        }
    }

    private bool HasStateMessage()
    {
        return stateMessages.Count > 0 &&
            Time.time >= stateMessages.Peek().deliveryTime;
    }

    private void UpdateClient(float deltaTime)
    {
        Rigidbody rigidbody = clientPlayer.GetComponent<Rigidbody>();

        if (HasStateMessage())
        {
            StateMessage message = stateMessages.Dequeue();
            while (HasStateMessage())
            {
                message = stateMessages.Dequeue();
            }

            latestTick = message.tick;

            if (errorCorrection)
            {
                int index = message.tick % BUFFER_SIZE;
                Vector3 positionError =
                    message.position - stateBuffer[index].position;
                float rotationError =
                    1.0f - Quaternion.Dot(
                        message.rotation, stateBuffer[index].rotation);

                if (positionError.sqrMagnitude > 0.0000001f ||
                    rotationError > 0.00001f)
                {
                    Debug.Log($"Correcting for error at tick {message.tick} " +
                        $"(rewinding {currentTick - message.tick} ticks)");

                    Vector3 previousPosition =
                        rigidbody.position + this.positionError;
                    Quaternion previousRotation =
                        rigidbody.rotation * this.rotationError;

                    rigidbody.position = message.position;
                    rigidbody.rotation = message.rotation;
                    rigidbody.velocity = message.velocity;
                    rigidbody.angularVelocity = message.angularVelocity;

                    int rewindTick = message.tick;
                    while (rewindTick < currentTick)
                    {
                        index = rewindTick % BUFFER_SIZE;

                        stateBuffer[index] = new()
                        {
                            position = rigidbody.position,
                            rotation = rigidbody.rotation
                        };

                        ApplyForce(clientPlayer, inputBuffer[index]);
                        clientScene.Simulate(deltaTime);

                        rewindTick++;
                    }

                    Vector3 positionDelta =
                        previousPosition - rigidbody.position;
                    if (positionDelta.sqrMagnitude >= 4.0f)
                    {
                        this.positionError = Vector3.zero;
                        this.rotationError = Quaternion.identity;
                    }
                    else
                    {
                        this.positionError = positionDelta;
                        this.rotationError =
                            Quaternion.Inverse(rigidbody.rotation) *
                            previousRotation;
                    }
                }
            }
        }

        if (correctionSmoothing)
        {
            this.positionError *= 0.9f;
            this.rotationError = Quaternion.Slerp(
                this.rotationError,
                Quaternion.identity,
                0.1f);
        }
        else
        {
            this.positionError = Vector3.zero;
            this.rotationError = Quaternion.identity;
        }

        clientPlayer.transform.position =
            rigidbody.position + this.positionError;
        clientPlayer.transform.rotation =
            rigidbody.rotation * this.rotationError;
    }
}