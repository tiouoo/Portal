using System;

namespace Portal.Bedrock.Core;

[Flags]
public enum MsiXVDVolumeAttributes : uint
{
	ReadOnly = 1u,
	EncryptionDisabled = 2u,
	DataIntegrityDisabled = 4u,
	LegacySectorSize = 8u,
	ResiliencyEnabled = 0x10u,
	SraReadOnly = 0x20u,
	RegionIdInXts = 0x40u,
	EraSpecific = 0x80u
}
