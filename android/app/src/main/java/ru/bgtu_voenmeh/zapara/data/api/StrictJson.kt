package ru.bgtu_voenmeh.zapara.data.api

import java.nio.ByteBuffer
import java.nio.charset.CharacterCodingException
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets

internal sealed class JsonValue {
    data class Obj(val fields: LinkedHashMap<String, JsonValue>) : JsonValue()
    data class Arr(val items: List<JsonValue>) : JsonValue()
    data class Str(val value: String) : JsonValue()
    data class Num(val raw: String) : JsonValue()
    data class Bool(val value: Boolean) : JsonValue()
    data object Null : JsonValue()
}

internal object StrictJson {
    fun parse(bytes: ByteArray, maxDepth: Int = 32): JsonValue {
        val decoder = StandardCharsets.UTF_8.newDecoder()
            .onMalformedInput(CodingErrorAction.REPORT)
            .onUnmappableCharacter(CodingErrorAction.REPORT)
        val text = try {
            decoder.decode(ByteBuffer.wrap(bytes)).toString()
        } catch (_: CharacterCodingException) {
            throw JsonFail()
        }
        return parse(text, maxDepth)
    }

    fun parse(text: String, maxDepth: Int = 32): JsonValue {
        val p = Parser(text, maxDepth)
        val value = p.parseValue(0)
        p.skipWs()
        if (p.i != text.length) throw JsonFail()
        return value
    }

    private class Parser(val s: String, val maxDepth: Int) {
        var i = 0
        fun skipWs() {
            while (i < s.length && s[i] in " \t\r\n") i++
        }

        fun parseValue(depth: Int): JsonValue {
            if (depth > maxDepth) throw JsonFail()
            skipWs()
            if (i >= s.length) throw JsonFail()
            return when (s[i]) {
                '{' -> parseObject(depth)
                '[' -> parseArray(depth)
                '"' -> JsonValue.Str(parseString())
                't' -> { expect("true"); JsonValue.Bool(true) }
                'f' -> { expect("false"); JsonValue.Bool(false) }
                'n' -> { expect("null"); JsonValue.Null }
                '-', in '0'..'9' -> parseNumber()
                else -> throw JsonFail()
            }
        }

        fun parseObject(depth: Int): JsonValue.Obj {
            expect("{")
            val fields = LinkedHashMap<String, JsonValue>()
            skipWs()
            if (i < s.length && s[i] == '}') {
                i++
                return JsonValue.Obj(fields)
            }
            while (true) {
                skipWs()
                if (i >= s.length || s[i] != '"') throw JsonFail()
                val name = parseString()
                if (name in fields) throw JsonFail()
                skipWs()
                expect(":")
                fields[name] = parseValue(depth + 1)
                skipWs()
                when {
                    i < s.length && s[i] == ',' -> i++
                    i < s.length && s[i] == '}' -> {
                        i++
                        return JsonValue.Obj(fields)
                    }
                    else -> throw JsonFail()
                }
            }
        }

        fun parseArray(depth: Int): JsonValue.Arr {
            expect("[")
            val items = ArrayList<JsonValue>()
            skipWs()
            if (i < s.length && s[i] == ']') {
                i++
                return JsonValue.Arr(items)
            }
            while (true) {
                items.add(parseValue(depth + 1))
                skipWs()
                when {
                    i < s.length && s[i] == ',' -> i++
                    i < s.length && s[i] == ']' -> {
                        i++
                        return JsonValue.Arr(items)
                    }
                    else -> throw JsonFail()
                }
            }
        }

        fun parseString(): String {
            expect("\"")
            val out = StringBuilder()
            while (i < s.length) {
                val c = s[i++]
                when (c) {
                    '"' -> return out.toString()
                    '\\' -> {
                        if (i >= s.length) throw JsonFail()
                        when (val e = s[i++]) {
                            '"', '\\', '/' -> out.append(e)
                            'b' -> out.append('\b')
                            'f' -> out.append('\u000C')
                            'n' -> out.append('\n')
                            'r' -> out.append('\r')
                            't' -> out.append('\t')
                            'u' -> {
                                if (i + 4 > s.length) throw JsonFail()
                                val hex = s.substring(i, i + 4)
                                i += 4
                                val cp = hex.toIntOrNull(16) ?: throw JsonFail()
                                out.append(cp.toChar())
                            }
                            else -> throw JsonFail()
                        }
                    }
                    in '\u0000'..'\u001F' -> throw JsonFail()
                    else -> out.append(c)
                }
            }
            throw JsonFail()
        }

        fun parseNumber(): JsonValue.Num {
            val start = i
            if (i < s.length && s[i] == '-') i++
            if (i >= s.length || s[i] !in '0'..'9') throw JsonFail()
            if (s[i] == '0') i++
            else while (i < s.length && s[i] in '0'..'9') i++
            if (i < s.length && s[i] == '.') {
                i++
                if (i >= s.length || s[i] !in '0'..'9') throw JsonFail()
                while (i < s.length && s[i] in '0'..'9') i++
            }
            if (i < s.length && (s[i] == 'e' || s[i] == 'E')) {
                i++
                if (i < s.length && (s[i] == '+' || s[i] == '-')) i++
                if (i >= s.length || s[i] !in '0'..'9') throw JsonFail()
                while (i < s.length && s[i] in '0'..'9') i++
            }
            return JsonValue.Num(s.substring(start, i))
        }

        fun expect(token: String) {
            skipWs()
            if (!s.startsWith(token, i)) throw JsonFail()
            i += token.length
        }
    }
}

internal class JsonFail : RuntimeException()

internal fun JsonValue.obj(): JsonValue.Obj = this as? JsonValue.Obj ?: throw JsonFail()
internal fun JsonValue.arr(): JsonValue.Arr = this as? JsonValue.Arr ?: throw JsonFail()
internal fun JsonValue.Obj.field(name: String): JsonValue = fields[name] ?: throw JsonFail()
internal fun JsonValue.Obj.text(name: String, max: Int = 2048, nonempty: Boolean = false): String {
    val v = field(name) as? JsonValue.Str ?: throw JsonFail()
    if (v.value.length > max || (nonempty && v.value.isBlank())) throw JsonFail()
    return v.value
}
internal fun JsonValue.Obj.nullableText(name: String, max: Int = 2048): String? =
    if (field(name) is JsonValue.Null) null else text(name, max)
internal fun JsonValue.Obj.int(name: String): Int {
    val n = field(name) as? JsonValue.Num ?: throw JsonFail()
    val v = n.raw.toIntOrNull() ?: throw JsonFail()
    if (n.raw != v.toString() && n.raw != "-0") throw JsonFail()
    return v
}
internal fun JsonValue.Obj.bool(name: String): Boolean {
    val v = field(name) as? JsonValue.Bool ?: throw JsonFail()
    return v.value
}
internal fun JsonValue.Obj.array(name: String, max: Int): JsonValue.Arr {
    val v = field(name).arr()
    if (v.items.size > max) throw JsonFail()
    return v
}
