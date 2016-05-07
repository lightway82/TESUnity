﻿using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ESM;

public class MorrowindDataReader : IDisposable
{
	public ESMFile MorrowindESMFile;
	public BSAFile MorrowindBSAFile;

	public ESMFile BloodmoonESMFile;
	public BSAFile BloodmoonBSAFile;

	public ESMFile TribunalESMFile;
	public BSAFile TribunalBSAFile;

	public MorrowindDataReader(string MorrowindFilePath)
	{
		MorrowindESMFile = new ESMFile(MorrowindFilePath + "/Morrowind.esm");
		MorrowindBSAFile = new BSAFile(MorrowindFilePath + "/Morrowind.bsa");

		BloodmoonESMFile = new ESMFile(MorrowindFilePath + "/Bloodmoon.esm");
		BloodmoonBSAFile = new BSAFile(MorrowindFilePath + "/Bloodmoon.bsa");

		TribunalESMFile = new ESMFile(MorrowindFilePath + "/Tribunal.esm");
		TribunalBSAFile = new BSAFile(MorrowindFilePath + "/Tribunal.bsa");
	}
	public void Close()
	{
		TribunalBSAFile.Close();
		TribunalESMFile.Close();

		BloodmoonBSAFile.Close();
		BloodmoonESMFile.Close();

		MorrowindBSAFile.Close();
		MorrowindESMFile.Close();
	}
	void IDisposable.Dispose()
	{
		Close();
	}

	public GameObject InstantiateNIF(string filePath)
	{
		var fileData = MorrowindBSAFile.LoadFileData(filePath);

		var file = new NIF.NiFile();
		file.Deserialize(new BinaryReader(new MemoryStream(fileData)));

		var objBuilder = new NIFObjectBuilder(file, this);
		return objBuilder.BuildObject();
	}
	public Texture2D LoadTexture(string textureName)
	{
		Texture2D loadedTexture;

		if(!loadedTextures.TryGetValue(textureName, out loadedTexture))
		{
			var filePath = "textures/" + textureName + ".dds";

			if(MorrowindBSAFile.ContainsFile(filePath))
			{
				var fileData = MorrowindBSAFile.LoadFileData(filePath);

				loadedTexture = TextureUtils.LoadDDSTexture(new MemoryStream(fileData));
				loadedTextures[textureName] = loadedTexture;
			}
			else
			{
				return null;
			}
		}

		return loadedTexture;
	}
	public void InstantiateExteriorCell(int x, int y)
	{
		var LAND = FindLANDRecord(x, y);

		if(LAND != null)
		{
			InstantiateLAND(LAND);
		}
	}

	private Dictionary<string, Texture2D> loadedTextures = new Dictionary<string, Texture2D>();

	private LTEXRecord FindLTEXRecord(int index)
	{
		foreach(var record in MorrowindESMFile.GetRecordsOfType<LTEXRecord>())
		{
			var LTEX = (LTEXRecord)record;

			if(LTEX.INTV.value == index)
			{
				return LTEX;
			}
		}

		return null;
	}
	private LANDRecord FindLANDRecord(int x, int y)
	{
		foreach(var record in MorrowindESMFile.GetRecordsOfType<LANDRecord>())
		{
			var LAND = (LANDRecord)record;

			if((LAND.INTV.value0 == x) && (LAND.INTV.value1 == y))
			{
				return LAND;
			}
		}

		return null;
	}
	private void InstantiateLAND(ESM.LANDRecord LAND)
	{
		// Don't create anything if the LAND doesn't have height data.
		if(LAND.VHGT == null)
		{
			return;
		}

		int LAND_SIZE = 65;
		float HEIGHT_SCALE = 1.0f / 16;
		int VTEX_SIZE = 16;

		var heights = new float[LAND_SIZE, LAND_SIZE];
		Debug.Assert(heights.Length == LAND.VHGT.heightOffsets.Length);

		// Read in the heights.
		float rowOffset = LAND.VHGT.referenceHeight;

		for(int y = 0; y < LAND_SIZE; y++)
		{
			rowOffset += LAND.VHGT.heightOffsets[y * LAND_SIZE];
			heights[y, 0] = rowOffset;

			float colOffset = rowOffset;

			for(int x = 1; x < LAND_SIZE; x++)
			{
				colOffset += LAND.VHGT.heightOffsets[(y * LAND_SIZE) + x];
				heights[y, x] = colOffset;
			}
		}

		// Change the heights to percentages.
		float minHeight, maxHeight;
		Utils.GetMinMax(heights, out minHeight, out maxHeight);

		for(int y = 0; y < LAND_SIZE; y++)
		{
			for(int x = 0; x < LAND_SIZE; x++)
			{
				heights[y, x] = Utils.ChangeRange(heights[y, x], minHeight, maxHeight, 0, 1);
			}
		}

		SplatPrototype[] splatPrototypes = null;
		float[,,] alphaMap = null;

		if(LAND.VTEX != null)
		{
			// Create splat prototypes.
			var splatPrototypeList = new List<SplatPrototype>();
			var texInd2splatInd = new Dictionary<ushort, int>();
			
			for(int i = 0; i < LAND.VTEX.textureIndices.Length; i++)
			{
				short textureIndex = (short)((short)LAND.VTEX.textureIndices[i] - 1);

				if(textureIndex < 0)
				{
					continue;
				}
				
				if(!texInd2splatInd.ContainsKey((ushort)textureIndex))
				{
					// Load terrain texture.
					var LTEX = FindLTEXRecord(textureIndex);

					if(textureIndex == 0)
					{
						int ii = 3;
					}

					var textureFileName = LTEX.DATA.value;
					var textureName = Path.GetFileNameWithoutExtension(textureFileName);
					var texture = LoadTexture(textureName);

					if(texture == null)
					{
						Debug.Log("asdf");
						texture = LoadTexture("tx_sand_01");
					}

					// Create the splat prototype.
					var splat = new SplatPrototype();
					splat.texture = texture;
					splat.smoothness = 0;
					splat.metallic = 0;

					// Update collections.
					var splatIndex = splatPrototypeList.Count;
					splatPrototypeList.Add(splat);
					texInd2splatInd.Add((ushort)textureIndex, splatIndex);
				}
			}

			splatPrototypes = splatPrototypeList.ToArray();

			// Create the alpha map.
			alphaMap = new float[VTEX_SIZE, VTEX_SIZE, splatPrototypes.Length];

			for(int y = 0; y < VTEX_SIZE; y++)
			{
				var yMajor = y / 4;
				var yMinor = y - (yMajor * 4);

				for(int x = 0; x < VTEX_SIZE; x++)
				{
					var xMajor = x / 4;
					var xMinor = x - (xMajor * 4);

					var texIndex = (short)((short)LAND.VTEX.textureIndices[(yMajor * 64) + (xMajor * 16) + (yMinor * 4) + xMinor] - 1);

					if(texIndex >= 0)
					{
						var splatIndex = texInd2splatInd[(ushort)texIndex];

						alphaMap[y, x, splatIndex] = 1;
					}
					else
					{
						alphaMap[y, x, 0] = 1;
					}
				}
			}
		}

		// Create the terrain.
		var heightRange = maxHeight - minHeight;
		var terrainPosition = new Vector3((LAND_SIZE - 1) * LAND.INTV.value0, minHeight * HEIGHT_SCALE, (LAND_SIZE - 1) * LAND.INTV.value1);
		var terrain = GameObjectUtils.CreateTerrain(heights, heightRange * HEIGHT_SCALE, 1, splatPrototypes, alphaMap, terrainPosition);
	}
}