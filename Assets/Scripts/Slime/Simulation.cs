using UnityEngine;
using UnityEngine.Experimental.Rendering;
using ComputeShaderUtility;

public class Simulation : MonoBehaviour
{
	public enum SpawnMode { Random, Point, InwardCircle, RandomCircle }

	const int updateKernel = 0;
	const int diffuseMapKernel = 1;

	public ComputeShader compute;
	public ComputeShader drawAgentsCS;

	public SlimeSettings settings;

	[Header("Display Settings")]
	public bool showAgentsOnly;
	public FilterMode filterMode = FilterMode.Point;
	public GraphicsFormat format = ComputeHelper.defaultGraphicsFormat;


	[SerializeField, HideInInspector] protected RenderTexture trailMap;
	[SerializeField, HideInInspector] protected RenderTexture diffusedTrailMap;
	[SerializeField, HideInInspector] protected RenderTexture displayTexture;

	ComputeBuffer agentBuffer;
	ComputeBuffer settingsBuffer;
	Texture2D colourMapTexture;

	protected virtual void Start()
	{
		Init();
	}

	static Texture3D CreateTexture3D()
    {
        // Configure the texture
        int size = 16;
        TextureFormat format = TextureFormat.RGBA32;
        TextureWrapMode wrapMode =  TextureWrapMode.Clamp;

        // Create the texture and apply the configuration
        Texture3D texture = new Texture3D(size, size, size, format, false);
        texture.wrapMode = wrapMode;

        // Create a 3-dimensional array to store color data
        Color[] colors = new Color[size * size * size];

        // Populate the array so that the x, y, and z values of the texture will map to red, blue, and green colors
        float inverseResolution = 1.0f / (size - 1.0f);
        for (int z = 0; z < size; z++)
        {
            int zOffset = z * size * size;
            for (int y = 0; y < size; y++)
            {
                int yOffset = y * size;
                for (int x = 0; x < size; x++)
                {
                    colors[x + yOffset + zOffset] = new Color(x * inverseResolution,
                        y * inverseResolution, z * inverseResolution, 1.0f);
                }
            }
        }

        // Copy the color values to the texture
        texture.SetPixels(colors);

        // Apply the changes to the texture and upload the updated texture to the GPU
        texture.Apply();        

        // Save the texture to your Unity Project
        // AssetDatabase.CreateAsset(texture, "Assets/Example3DTexture.asset");
	return texture;
    }


	void Init()
	{
		// Create render textures
		ComputeHelper.CreateRenderTexture(ref trailMap, settings.width, settings.height, settings.depth, filterMode, format);
		ComputeHelper.CreateRenderTexture(ref diffusedTrailMap, settings.width, settings.height, settings.depth, filterMode, format);
		ComputeHelper.CreateRenderTexture(ref displayTexture, settings.width, settings.height, settings.depth, filterMode, format);
		transform.GetComponentInChildren<MeshRenderer>().material.SetTexture("_MainTex", displayTexture);
		// transform.GetComponentInChildren<MeshRenderer>().material.SetTexture("_MainTex", CreateTexture3D());
		transform.GetComponentInChildren<MeshRenderer>().material.SetFloat("_ResWidth", settings.width);
		transform.GetComponentInChildren<MeshRenderer>().material.SetFloat("_ResHeight", settings.height);
		transform.GetComponentInChildren<MeshRenderer>().material.SetFloat("_ResDepth", settings.depth);

		// Create agents with initial positions and angles
		Agent[] agents = new Agent[settings.numAgents];
		for (int i = 0; i < agents.Length; i++)
		{
			Vector3 centre = new Vector3(settings.width / 2, settings.height / 2, settings.depth / 2);
			// centre = new Vector3(settings.width / 2, settings.height / 2, 0);
			Vector3 startPos = Vector3.zero;
			Vector2 angles = new Vector2(0,Mathf.PI / 2);//Phi(x-y), theta(z)
			Vector2 randomAngles = new Vector2(Random.value * Mathf.PI * 2, Random.value * Mathf.PI);			

			if (settings.spawnMode == SpawnMode.Point)
			{
				startPos = centre;
				angles = randomAngles;
			}
			else if (settings.spawnMode == SpawnMode.Random)
			{
				startPos = new Vector2(Random.Range(0, settings.width), Random.Range(0, settings.height));
				angles = randomAngles;
			}
			else if (settings.spawnMode == SpawnMode.InwardCircle)
			{
				startPos = centre + Random.insideUnitSphere * settings.height * 0.5f;
				Vector3 loc = (centre - startPos);

				float phi = Mathf.Atan2(loc.normalized.y, loc.normalized.x);
				float theta = Mathf.Atan2(Mathf.Sqrt(Mathf.Pow(loc.x,2) + Mathf.Pow(loc.y,2)), loc.z);
				angles = new Vector2(phi, theta);
			}
			else if (settings.spawnMode == SpawnMode.RandomCircle)
			{
				startPos = centre + Random.insideUnitSphere * settings.height * 0.15f;
				angles = randomAngles;
			}

			Vector3Int speciesMask;
			int speciesIndex = 0;
			int numSpecies = settings.speciesSettings.Length;

			if (numSpecies == 1)
			{
				speciesMask = Vector3Int.one;
			}
			else
			{
				int species = Random.Range(1, numSpecies + 1);
				speciesIndex = species - 1;
				speciesMask = new Vector3Int((species == 1) ? 1 : 0, (species == 2) ? 1 : 0, (species == 3) ? 1 : 0);
			}

			agents[i] = new Agent() { position = startPos, angles = angles, speciesMask = speciesMask, speciesIndex = speciesIndex };
		}

		ComputeHelper.CreateAndSetBuffer<Agent>(ref agentBuffer, agents, compute, "agents", updateKernel);
		compute.SetInt("numAgents", settings.numAgents);
		drawAgentsCS.SetBuffer(0, "agents", agentBuffer);
		drawAgentsCS.SetInt("numAgents", settings.numAgents);


		compute.SetInt("width", settings.width);
		compute.SetInt("height", settings.height);
		compute.SetInt("depth", settings.depth);
	}

	void FixedUpdate()
	{
		for (int i = 0; i < settings.stepsPerFrame; i++)
		{
			RunSimulation();
		}
	}

	void LateUpdate()
	{
		if (showAgentsOnly)
		{
			ComputeHelper.ClearRenderTexture(displayTexture);

			drawAgentsCS.SetTexture(0, "TargetTexture", displayTexture);
			ComputeHelper.Dispatch(drawAgentsCS, settings.numAgents, 1, 1, 0);
		}
		else
		{
			ComputeHelper.CopyRenderTexture(trailMap, displayTexture);
		}
	}

	void RunSimulation()
	{

		var speciesSettings = settings.speciesSettings;
		ComputeHelper.CreateStructuredBuffer(ref settingsBuffer, speciesSettings);
		compute.SetBuffer(0, "speciesSettings", settingsBuffer);


		// Assign textures
		compute.SetTexture(updateKernel, "TrailMap", trailMap);
		compute.SetTexture(diffuseMapKernel, "TrailMap", trailMap);
		compute.SetTexture(diffuseMapKernel, "DiffusedTrailMap", diffusedTrailMap);

		// Assign settings
		compute.SetFloat("deltaTime", Time.fixedDeltaTime);
		compute.SetFloat("time", Time.fixedTime);

		compute.SetFloat("trailWeight", settings.trailWeight);
		compute.SetFloat("decayRate", settings.decayRate);
		compute.SetFloat("diffuseRate", settings.diffuseRate);


		ComputeHelper.Dispatch(compute, settings.numAgents, 1, 1, kernelIndex: updateKernel);
		ComputeHelper.Dispatch(compute, settings.width, settings.height, 1, kernelIndex: diffuseMapKernel);

		ComputeHelper.CopyRenderTexture(diffusedTrailMap, trailMap);
	}

	void OnDestroy()
	{
		ComputeHelper.Release(agentBuffer, settingsBuffer);
	}

	public struct Agent
	{
		public Vector3 position;
		public Vector2 angles;
		public Vector3Int speciesMask;
		int unusedSpeciesChannel;
		public int speciesIndex;
	}
}
