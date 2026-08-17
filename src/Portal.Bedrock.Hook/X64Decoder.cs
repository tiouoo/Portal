namespace Portal.Bedrock.Hook;

internal static class X64Decoder
{
	public unsafe static bool TryDecode(byte* code, int maxBytes, out int length, out int ripDispOffset)
	{
		length = 0;
		ripDispOffset = -1;
		int num = 0;
		bool flag = false;
		while (num < maxBytes)
		{
			bool flag2;
			switch (code[num])
			{
			case 102:
				flag = true;
				goto IL_007a;
			case 38:
			case 46:
			case 54:
			case 62:
			case 100:
			case 101:
			case 103:
			case 240:
			case 242:
			case 243:
				flag2 = true;
				goto IL_0076;
			default:
				{
					flag2 = false;
					goto IL_0076;
				}
				IL_0076:
				if (!flag2)
				{
					break;
				}
				goto IL_007a;
			}
			break;
			IL_007a:
			if (++num > 14)
			{
				return false;
			}
		}
		if (num >= maxBytes)
		{
			return false;
		}
		bool flag3 = false;
		bool rexR = false;
		bool rexX = false;
		bool rexB = false;
		byte b = code[num];
		if ((b & 0xF0) == 64)
		{
			flag3 = (b & 8) != 0;
			rexR = (b & 4) != 0;
			rexX = (b & 2) != 0;
			rexB = (b & 1) != 0;
			if (++num >= maxBytes)
			{
				return false;
			}
			b = code[num];
		}
		if ((uint)(b - 196) <= 1u)
		{
			return false;
		}
		int num2 = num;
		int num5;
		switch (b)
		{
		case 15:
		{
			if (++num >= maxBytes)
			{
				return false;
			}
			byte b2 = code[num];
			bool flag2;
			switch (b2)
			{
			case 5:
			case 6:
			case 7:
			case 8:
			case 9:
			case 11:
			case 14:
			case 48:
			case 49:
			case 50:
			case 51:
			case 52:
			case 53:
			case 55:
			case 119:
			case 162:
			case 170:
			case 185:
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			if (flag2)
			{
				num5 = 2;
				break;
			}
			if (b2 >= 128 && b2 <= 143)
			{
				num5 = 6;
				break;
			}
			if (!DecodeModRM(code, num + 1, flag3, rexR, rexX, rexB, out var afterEnd2, out ripDispOffset))
			{
				return false;
			}
			switch (b2)
			{
			case 112:
			case 113:
			case 114:
			case 115:
			case 164:
			case 172:
			case 186:
			case 196:
			case 197:
			case 198:
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			bool flag4 = flag2;
			num5 = afterEnd2 - num2 + (flag4 ? 1 : 0);
			break;
		}
		case 144:
		case 152:
		case 153:
		case 155:
		case 156:
		case 157:
		case 158:
		case 159:
		case 195:
		case 201:
		case 203:
		case 204:
		case 206:
		case 207:
		case 244:
		case 245:
		case 248:
		case 249:
		case 250:
		case 251:
		case 252:
		case 253:
		case 80:
		case 81:
		case 82:
		case 83:
		case 84:
		case 85:
		case 86:
		case 87:
		case 88:
		case 89:
		case 90:
		case 91:
		case 92:
		case 93:
		case 94:
		case 95:
			num5 = 1;
			break;
		case 106:
		case 112:
		case 113:
		case 114:
		case 115:
		case 116:
		case 117:
		case 118:
		case 119:
		case 120:
		case 121:
		case 122:
		case 123:
		case 124:
		case 125:
		case 126:
		case 127:
		case 205:
		case 212:
		case 213:
		case 224:
		case 225:
		case 226:
		case 227:
		case 235:
			num5 = 2;
			break;
		case 104:
		case 232:
		case 233:
			num5 = 5;
			break;
		case 194:
		case 202:
			num5 = 3;
			break;
		case 200:
			num5 = 4;
			break;
		case 176:
		case 177:
		case 178:
		case 179:
		case 180:
		case 181:
		case 182:
		case 183:
			num5 = 2;
			break;
		case 184:
		case 185:
		case 186:
		case 187:
		case 188:
		case 189:
		case 190:
		case 191:
			num5 = (flag3 ? 9 : (flag ? 3 : 5));
			break;
		case 160:
		case 161:
		case 162:
		case 163:
			num5 = 9;
			break;
		case 164:
		case 165:
		case 166:
		case 167:
		case 172:
		case 173:
		case 174:
		case 175:
		case 236:
		case 237:
		case 238:
		case 239:
			num5 = 1;
			break;
		case 168:
		case 169:
			num5 = (flag ? 3 : (flag3 ? 5 : 3));
			break;
		case 228:
		case 229:
		case 230:
		case 231:
			num5 = 2;
			break;
		default:
		{
			if (!RequiresModRM(b))
			{
				return false;
			}
			if (!DecodeModRM(code, num2 + 1, flag3, rexR, rexX, rexB, out var afterEnd, out ripDispOffset))
			{
				return false;
			}
			int num3;
			switch (b)
			{
			case 107:
			case 128:
			case 130:
			case 131:
				num3 = 1;
				break;
			case 105:
			case 129:
			case 199:
			case 247:
				num3 = 4;
				break;
			case 192:
			case 193:
			case 198:
			case 246:
				num3 = 1;
				break;
			default:
				num3 = 0;
				break;
			}
			int num4 = num3;
			num5 = afterEnd - num2 + num4;
			break;
		}
		}
		if (num5 <= 0 || num2 + num5 > maxBytes)
		{
			return false;
		}
		length = num2 + num5;
		ripDispOffset = ((ripDispOffset >= 0) ? ripDispOffset : (-1));
		return true;
	}

	private static bool RequiresModRM(byte op)
	{
		if (op <= 5)
		{
			return true;
		}
		bool flag;
		switch (op)
		{
		case 8:
		case 9:
		case 10:
		case 11:
		case 12:
		case 13:
		case 16:
		case 17:
		case 18:
		case 19:
		case 20:
		case 21:
		case 24:
		case 25:
		case 26:
		case 27:
		case 28:
		case 29:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return true;
		}
		switch (op)
		{
		case 32:
		case 33:
		case 34:
		case 35:
		case 36:
		case 37:
		case 40:
		case 41:
		case 42:
		case 43:
		case 44:
		case 45:
		case 48:
		case 49:
		case 50:
		case 51:
		case 52:
		case 53:
		case 56:
		case 57:
		case 58:
		case 59:
		case 60:
		case 61:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return true;
		}
		if ((op == 99 || op == 105 || op == 107) ? true : false)
		{
			return true;
		}
		if (op >= 128 && op <= 143)
		{
			return true;
		}
		if (((uint)(op - 192) <= 1u || (uint)(op - 198) <= 1u) ? true : false)
		{
			return true;
		}
		switch (op)
		{
		case 208:
		case 209:
		case 210:
		case 211:
		case 216:
		case 217:
		case 218:
		case 219:
		case 220:
		case 221:
		case 222:
		case 223:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return true;
		}
		if (((uint)(op - 246) <= 1u || (uint)(op - 254) <= 1u) ? true : false)
		{
			return true;
		}
		return false;
	}

	private unsafe static bool DecodeModRM(byte* code, int modrmIndex, bool rexW, bool rexR, bool rexX, bool rexB, out int afterEnd, out int ripDispOffset)
	{
		afterEnd = 0;
		ripDispOffset = -1;
		byte num = code[modrmIndex];
		int num2 = (num >> 6) & 3;
		int num3 = num & 7;
		int num4 = 1;
		if (num2 == 3)
		{
			afterEnd = modrmIndex + num4;
			return true;
		}
		if (num2 == 0 && num3 == 5)
		{
			ripDispOffset = modrmIndex + num4;
			num4 += 4;
		}
		else if (num3 == 4)
		{
			byte b = code[modrmIndex + 1];
			num4++;
			if (num2 == 0 && (b & 7) == 5)
			{
				num4 += 4;
			}
			else
			{
				switch (num2)
				{
				case 1:
					num4++;
					break;
				case 2:
					num4 += 4;
					break;
				}
			}
		}
		else
		{
			switch (num2)
			{
			case 1:
				num4++;
				break;
			case 2:
				num4 += 4;
				break;
			}
		}
		afterEnd = modrmIndex + num4;
		return true;
	}
}
