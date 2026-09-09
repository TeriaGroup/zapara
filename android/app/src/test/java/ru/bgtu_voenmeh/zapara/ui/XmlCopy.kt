package ru.bgtu_voenmeh.zapara.ui

import org.w3c.dom.Element
import java.io.File
import javax.xml.parsers.DocumentBuilderFactory

object XmlCopy : UiCopy {
    private val values: Map<String, String> by lazy {
        val file = File("src/main/res/values/strings.xml")
        val doc = DocumentBuilderFactory.newInstance().newDocumentBuilder().parse(file)
        val nodes = doc.getElementsByTagName("string")
        (0 until nodes.length).associate { i ->
            val node = nodes.item(i) as Element
            node.getAttribute("name") to node.textContent
        }
    }

    override fun get(name: String, vararg args: Any?): String {
        val raw = values[name] ?: error("missing string $name")
        if (args.isEmpty()) return raw
        return raw.format(*args)
    }
}
