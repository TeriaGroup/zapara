package ru.bgtu_voenmeh.zapara.ui.groups

internal enum class QuoteTarget { Loaded, Deleted, Earlier }

internal fun quoteTarget(messages: List<GroupMessageUi>, id: String): QuoteTarget =
    messages.firstOrNull { it.id == id }?.let { if (it.deleted) QuoteTarget.Deleted else QuoteTarget.Loaded }
        ?: QuoteTarget.Earlier
