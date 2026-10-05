package ru.bgtu_voenmeh.zapara.data.profiles

data class ProfileDescriptor(
    val isGuest: Boolean,
    val userId: String?,
    val serverKey: String?,
    val databaseName: String
) {
    companion object {
        const val GUEST_DB = "zapara.db"

        fun guest() = ProfileDescriptor(true, null, null, GUEST_DB)

        fun account(serverKey: String, userId: String): ProfileDescriptor {
            if (serverKey.length != 64 || serverKey.any { !it.isDigit() && it !in 'A'..'F' } || userId == "00000000-0000-0000-0000-000000000000") {
                throw IllegalArgumentException("Недопустимый идентификатор профиля.")
            }
            val uuid = ru.bgtu_voenmeh.zapara.data.accounts.AccountValidation.id(userId)
            return ProfileDescriptor(false, uuid, serverKey, "profiles/$serverKey/$uuid/zapara.db")
        }
    }
}
