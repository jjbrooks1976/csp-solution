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
    public const float MOVE_FORCE = 0.5f;
    public const float JUMP_THRESHOLD = 0.75f;

    public GameObject clientPlayer;
    public GameObject serverPlayer;
    public bool errorCorrection = true;
    public bool correctionSmoothing = true;
    public bool redundantInput = true;
    public float networkLatency = 0.1f;
    public float packetLoss = 0.05f;
    public uint snapshotRate = 0;

    private const int BUFFER_SIZE = 1024;

    private float deltaTime;
    private float currentTime;
    private int currentTick;
    private int latestTick;
    private UserInput[] inputBuffer; //predicted inputs
    private ClientState[] stateBuffer; //predicted states
    private Queue<StateMessage> stateMessages;
    private Vector3 positionError;
    private Quaternion rotationError;
    private Rigidbody clientBody;
    private PhysicsScene clientScene;

    private int serverTick;
    private Queue<InputMessage> inputMessages;
    private Rigidbody serverBody;
    private PhysicsScene serverScene;

    void Start()
    {
        deltaTime = Time.fixedDeltaTime;
        currentTime = 0.0f;
        currentTick = 0;
        latestTick = 0;
        inputBuffer = new UserInput[BUFFER_SIZE];
        stateBuffer = new ClientState[BUFFER_SIZE];
        stateMessages = new Queue<StateMessage>();
        positionError = Vector3.zero;
        rotationError = Quaternion.identity;
        clientBody = clientPlayer.GetComponent<Rigidbody>();

        Scene scene1 = SceneManager.LoadScene("Background",
            new LoadSceneParameters()
            {
                loadSceneMode = LoadSceneMode.Additive,
                localPhysicsMode = LocalPhysicsMode.Physics3D
            });

        SceneManager.MoveGameObjectToScene(clientPlayer, scene1);
        clientScene = scene1.GetPhysicsScene();

        serverTick = 0;
        inputMessages = new Queue<InputMessage>();
        serverBody = serverPlayer.GetComponent<Rigidbody>();

        Scene scene2 = SceneManager.LoadScene("Background",
            new LoadSceneParameters()
            {
                loadSceneMode = LoadSceneMode.Additive,
                localPhysicsMode = LocalPhysicsMode.Physics3D
            });

        SceneManager.MoveGameObjectToScene(serverPlayer, scene2);
        serverScene = scene2.GetPhysicsScene();
    }

    void Update()
    {
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
                position = clientBody.position,
                rotation = clientBody.rotation
            };

            ApplyForce(clientBody, input);
            clientScene.Simulate(deltaTime);

            SendInput();

            currentTick++;
        }

        UpdateClient();
        UpdateServer();
    }

    private void ApplyForce(Rigidbody rigidbody, UserInput input)
    {
        if (input.up)
        {
            rigidbody.AddForce(
                Camera.main.transform.forward * MOVE_FORCE,
                ForceMode.Impulse);
        }

        if (input.down)
        {
            rigidbody.AddForce(
                -Camera.main.transform.forward * MOVE_FORCE,
                ForceMode.Impulse);
        }

        if (input.right)
        {
            rigidbody.AddForce(
                Camera.main.transform.right * MOVE_FORCE,
                ForceMode.Impulse);
        }

        if (input.left)
        {
            rigidbody.AddForce(
                -Camera.main.transform.right * MOVE_FORCE,
                ForceMode.Impulse);
        }

        if (input.jump && rigidbody.position.y <= JUMP_THRESHOLD)
        {
            rigidbody.AddForce(
                Camera.main.transform.up * MOVE_FORCE,
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

    private void UpdateServer()
    {
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
                    ApplyForce(serverBody, inputMessage.inputs[i]);
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
                            position = serverBody.position,
                            rotation = serverBody.rotation,
                            velocity = serverBody.velocity,
                            angularVelocity = serverBody.angularVelocity
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

    private void UpdateClient()
    {
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
                float rotationError = 1.0f - Quaternion.Dot(
                    message.rotation,
                    stateBuffer[index].rotation);

                if (positionError.sqrMagnitude > 0.0000001f ||
                    rotationError > 0.00001f)
                {
                    Debug.Log($"Correct error at tick {message.tick} " +
                        $"(rewinding {currentTick - message.tick} ticks)");

                    Vector3 previousPosition =
                        clientBody.position + this.positionError;
                    Quaternion previousRotation =
                        clientBody.rotation * this.rotationError;

                    clientBody.position = message.position;
                    clientBody.rotation = message.rotation;
                    clientBody.velocity = message.velocity;
                    clientBody.angularVelocity = message.angularVelocity;

                    int rewindTick = message.tick;
                    while (rewindTick < currentTick)
                    {
                        index = rewindTick % BUFFER_SIZE;

                        stateBuffer[index] = new()
                        {
                            position = clientBody.position,
                            rotation = clientBody.rotation
                        };

                        ApplyForce(clientBody, inputBuffer[index]);
                        clientScene.Simulate(deltaTime);

                        rewindTick++;
                    }

                    Vector3 positionDelta =
                        previousPosition - clientBody.position;
                    if (positionDelta.sqrMagnitude >= 4.0f)
                    {
                        this.positionError = Vector3.zero;
                        this.rotationError = Quaternion.identity;
                    }
                    else
                    {
                        this.positionError = positionDelta;
                        this.rotationError =
                            Quaternion.Inverse(clientBody.rotation) *
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

        clientBody.position += this.positionError;
        clientBody.rotation *= this.rotationError;
    }
}