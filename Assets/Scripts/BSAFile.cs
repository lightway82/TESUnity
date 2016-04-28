﻿using System;
using System.Collections.Generic;
using System.IO;

public class BSAFile : IDisposable
{
	public struct FileNameHash
	{
		public uint value1;
		public uint value2;
	}

	public struct FileMetadata
	{
		public uint size;
		public uint offsetInDataSection;
		public string name;
		public FileNameHash nameHash;
	}

	/* Public */
	public byte[] version; // 4 bytes
	public FileMetadata[] fileMetadatas;

	public bool isAtEOF
	{
		get
		{
			return reader.BaseStream.Position >= reader.BaseStream.Length;
		}
	}

	public BSAFile(string filePath)
	{
		reader = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read));

		ReadMetadata();
	}
	public void Close()
	{
		reader.Close();
	}
	void IDisposable.Dispose()
	{
		Close();
	}

	public byte[] LoadFileData(FileMetadata fileMetadata)
	{
		reader.BaseStream.Position = fileDataSectionPostion + fileMetadata.offsetInDataSection;

		return reader.ReadBytes((int)fileMetadata.size);
	}

	/* Private */
	private BinaryReader reader;
	private long hashTablePosition;
	private long fileDataSectionPostion;

	private void ReadMetadata()
	{
		// Read the header.
		version = reader.ReadBytes(4);
		uint hashTableOffsetFromEndOfHeader = reader.ReadUInt32(); // minus header size (12 bytes)
		uint fileCount = reader.ReadUInt32();

		// Calculate some useful values.
		var headerSize = reader.BaseStream.Position;
		hashTablePosition = headerSize + hashTableOffsetFromEndOfHeader;
		fileDataSectionPostion = hashTablePosition + (8 * fileCount);

		fileMetadatas = new FileMetadata[fileCount];

		// Read file sizes/offsets.
		for(int i = 0; i < fileCount; i++)
		{
			fileMetadatas[i].size = reader.ReadUInt32();
			fileMetadatas[i].offsetInDataSection = reader.ReadUInt32();
		}

		// Read filename offsets.
		var filenameOffsets = new uint[fileCount]; // relative offset in filenames section

		for(int i = 0; i < fileCount; i++)
		{
			filenameOffsets[i] = reader.ReadUInt32();
		}

		// Read filenames.
		var filenamesSectionStartPos = reader.BaseStream.Position;
		var filenameBuffer = new List<byte>(64);

		for(int i = 0; i < fileCount; i++)
		{
			reader.BaseStream.Position = filenamesSectionStartPos + filenameOffsets[i];

			filenameBuffer.Clear();
			byte curCharAsByte;

			while((curCharAsByte = reader.ReadByte()) != 0)
			{
				filenameBuffer.Add(curCharAsByte);
			}

			fileMetadatas[i].name = System.Text.Encoding.ASCII.GetString(filenameBuffer.ToArray());
		}

		// Read filename hashes.
		reader.BaseStream.Position = hashTablePosition;

		for(int i = 0; i < fileCount; i++)
		{
			fileMetadatas[i].nameHash.value1 = reader.ReadUInt32();
			fileMetadatas[i].nameHash.value2 = reader.ReadUInt32();
		}

		reader.BaseStream.Position = fileDataSectionPostion;
	}
	private FileNameHash HashFilePath(string filePath)
	{
		FileNameHash hash = new FileNameHash();

		uint len = (uint)filePath.Length;
		uint l = (len >> 1);
		int off, i;
		uint sum, temp, n;

		sum = 0;
		off = 0;

		for(i = 0; i < l; i++)
		{
			sum ^= (uint)(filePath[i]) << (off & 0x1F);
			off += 8;
		}

		hash.value1 = sum;

		sum = 0;
		off = 0;

		for(; i < len; i++)
		{
			temp = (uint)(filePath[i]) << (off & 0x1F);
			sum ^= temp;
			n = temp & 0x1F;
			sum = (sum << (32 - (int)n)) | (sum >> (int)n);  // binary "rotate right"
			off += 8;
		}

		hash.value2 = sum;

		return hash;
	}
}