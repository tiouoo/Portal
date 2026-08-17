using System;

namespace Portal.Bedrock.Core;

[Flags]
public enum MsiXvcAreaPresenceInfo : byte
{
	IsPresent = 1,
	IsAvailable = 2,
	Disc1 = 0x10,
	Disc2 = 0x20,
	Disc3 = Disc1 | Disc2,
	Disc4 = 0x40,
	Disc5 = Disc1 | Disc4,
	Disc6 = Disc2 | Disc4,
	Disc7 = Disc3 | Disc4,
	Disc8 = 0x80,
	Disc9 = Disc1 | Disc8,
	Disc10 = Disc2 | Disc8,
	Disc11 = Disc3 | Disc8,
	Disc12 = Disc4 | Disc8,
	Disc13 = Disc5 | Disc8,
	Disc14 = Disc6 | Disc8,
	Disc15 = Disc7 | Disc8
}
