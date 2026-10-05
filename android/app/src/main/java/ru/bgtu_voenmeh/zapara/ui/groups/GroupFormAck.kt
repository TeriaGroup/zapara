package ru.bgtu_voenmeh.zapara.ui.groups

import ru.bgtu_voenmeh.zapara.data.communities.GroupForm

internal fun mergeFormSubmitAck(current: List<GroupForm>, acknowledged: GroupForm): List<GroupForm> {
    var replaced = false
    val merged = current.map { form ->
        if (!replaced && form.formId == acknowledged.formId) {
            replaced = true
            val currentUpdatedAt = form.ownResponse?.updatedAt
            val acknowledgedUpdatedAt = acknowledged.ownResponse?.updatedAt
            if (currentUpdatedAt != null && acknowledgedUpdatedAt != null && currentUpdatedAt.isAfter(acknowledgedUpdatedAt))
                form else acknowledged
        } else form
    }
    return if (replaced) merged else merged + acknowledged
}

internal fun mergeFormRefreshWithAck(refreshed: List<GroupForm>, acknowledged: GroupForm): List<GroupForm> {
    var mergedAck = false
    val merged = refreshed.map { form ->
        if (!mergedAck && form.formId == acknowledged.formId) {
            mergedAck = true
            val currentTime = form.ownResponse?.updatedAt
            val ackTime = acknowledged.ownResponse?.updatedAt
            val response = if (currentTime != null && ackTime != null && currentTime.isAfter(ackTime))
                form.ownResponse else acknowledged.ownResponse
            form.copy(ownResponse = response, responseCount = maxOf(form.responseCount, acknowledged.responseCount))
        } else form
    }
    return if (mergedAck) merged else refreshed
}
