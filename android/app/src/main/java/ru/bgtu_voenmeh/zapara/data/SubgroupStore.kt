package ru.bgtu_voenmeh.zapara.data

import android.content.Context
import org.json.JSONObject

class SubgroupStore(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences(FILE, Context.MODE_PRIVATE)

    fun read(profileKey: String, groupId: String): Map<String, String> {
        val raw = prefs.getString(key(profileKey, groupId), null) ?: return emptyMap()
        return runCatching {
            val obj = JSONObject(raw)
            obj.keys().asSequence().associateWith { obj.getString(it) }
        }.getOrDefault(emptyMap())
    }

    fun select(profileKey: String, groupId: String, streamId: String, optionId: String) {
        val next = read(profileKey, groupId).toMutableMap()
        if (next[streamId] == optionId) next.remove(streamId) else next[streamId] = optionId
        val obj = JSONObject()
        next.forEach { (stream, option) -> obj.put(stream, option) }
        prefs.edit().putString(key(profileKey, groupId), obj.toString()).apply()
    }

    private fun key(profileKey: String, groupId: String) = profileKey + "\u0000" + groupId

    private companion object {
        const val FILE = "zapara_subgroups"
    }
}
