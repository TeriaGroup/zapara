package ru.bgtu_voenmeh.zapara.ui.chat

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Matrix
import android.media.ExifInterface
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Icon
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import ru.bgtu_voenmeh.zapara.R
import ru.bgtu_voenmeh.zapara.data.api.HttpBodies
import ru.bgtu_voenmeh.zapara.data.avatars.*
import ru.bgtu_voenmeh.zapara.ui.components.ZBottomSheet
import ru.bgtu_voenmeh.zapara.ui.theme.ZButton
import ru.bgtu_voenmeh.zapara.ui.theme.Zapara
import java.io.ByteArrayInputStream
import java.io.ByteArrayOutputStream

val LocalAvatarStore = staticCompositionLocalOf<AvatarStore?> { null }

@Composable
fun ChatAvatar(name: String, target: AvatarTarget?, size: Dp = 40.dp, modifier: Modifier = Modifier) {
    val store = LocalAvatarStore.current
    val version = store?.revision?.collectAsStateWithLifecycle()?.value ?: 0L
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    val bitmap by produceState<Bitmap?>(null, store, target, version, lifecycle) {
        value = null
        if (store == null || target == null || target.id.isBlank()) return@produceState
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                val bytes = withContext(Dispatchers.IO) { store.load(target) }
                value = withContext(Dispatchers.Default) {
                    bytes?.let {
                        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                        BitmapFactory.decodeByteArray(it, 0, it.size, bounds)
                        if (bounds.outWidth !in 1..512 || bounds.outHeight !in 1..512) null
                        else BitmapFactory.decodeByteArray(it, 0, it.size)
                    }
                }
                delay(60_000)
            }
        }
    }
    Box(modifier.size(size).clip(CircleShape).background(Zapara.colors.chip)
        .border(Zapara.space.hairline, Zapara.colors.line, CircleShape), contentAlignment = Alignment.Center) {
        val image = bitmap
        if (image != null) Image(image.asImageBitmap(), stringResource(R.string.avatar_photo, name),
            Modifier.fillMaxSize(), contentScale = ContentScale.Crop)
        // #108 / AN-13: у группы без фото — значок группы, а не две буквы из одного слова («И8» у «И831Б»).
        else if (target?.kind == AvatarKind.Group) Icon(painterResource(R.drawable.ic_users), contentDescription = null,
            tint = Zapara.colors.text2, modifier = Modifier.size(size * 0.5f).testTag("Avatar.GroupGlyph"))
        else Text(avatarInitials(name), style = if (size < 36.dp) Zapara.typography.caption else Zapara.typography.bodyStrong,
            color = Zapara.colors.text2)
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
fun AvatarEditor(name: String, target: AvatarTarget, modifier: Modifier = Modifier, enabled: Boolean = true, compact: Boolean = false) {
    val store = LocalAvatarStore.current ?: return
    val context = LocalContext.current
    val coroutine = rememberCoroutineScope()
    var busy by remember(store, target) { mutableStateOf(false) }
    var feedback by remember(store, target) { mutableStateOf<String?>(null) }
    var failed by remember(store, target) { mutableStateOf(false) }
    var confirm by remember(store, target) { mutableStateOf(false) }
    var menuOpen by remember(store, target) { mutableStateOf(false) }
    var pickingFor by remember { mutableStateOf<AvatarTarget?>(null) }
    fun perform(action: suspend () -> Unit, success: Int) {
        if (busy) return
        busy = true
        feedback = null
        failed = false
        coroutine.launch {
            try { withContext(Dispatchers.IO) { action() }; feedback = context.getString(success) }
            catch (cancelled: CancellationException) { throw cancelled }
            catch (error: Exception) {
                failed = true
                feedback = context.getString(when (error) {
                    is AvatarFailure -> when (error.status) {
                        401 -> R.string.avatar_session
                        403 -> R.string.avatar_forbidden
                        404, 405, 501 -> R.string.avatar_unavailable
                        400, 413, 415 -> R.string.avatar_invalid
                        else -> R.string.avatar_failed
                    }
                    is IllegalArgumentException, is ru.bgtu_voenmeh.zapara.data.api.HttpBodyTooLargeException -> R.string.avatar_invalid
                    else -> R.string.avatar_failed
                })
            } finally { busy = false }
        }
    }
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.GetContent()) { uri ->
        val origin = pickingFor
        pickingFor = null
        if (uri != null && origin != null && origin == target) perform({ store.upload(origin, prepareAvatar(context, uri)) }, R.string.avatar_saved)
    }
    if (compact) {
        val menuLabel = stringResource(if (target.kind == AvatarKind.Group) R.string.avatar_group else R.string.avatar_profile)
        Box(modifier.size(56.dp).clickable(enabled = enabled && !busy, role = Role.Button, onClick = { menuOpen = true })
            .semantics { contentDescription = menuLabel }.testTag("Avatar.OpenMenu")) {
            ChatAvatar(name, target, 56.dp)
            Box(Modifier.align(Alignment.BottomEnd).size(20.dp).clip(CircleShape)
                .background(Zapara.colors.surface).border(Zapara.space.hairline, Zapara.colors.line, CircleShape),
                contentAlignment = Alignment.Center) {
                Icon(painterResource(R.drawable.ic_avatar_photo), contentDescription = null,
                    modifier = Modifier.size(13.dp), tint = Zapara.colors.text1)
            }
        }
        if (menuOpen) ZBottomSheet(onDismiss = { menuOpen = false }, tag = "Avatar.Menu", scrollable = true, canDismiss = { !busy }) {
            Text(menuLabel, style = Zapara.typography.section)
            Text(stringResource(if (target.kind == AvatarKind.Group) R.string.avatar_group_hint else R.string.avatar_hint),
                style = Zapara.typography.caption, color = Zapara.colors.text2)
            if (busy) Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                Text(stringResource(R.string.avatar_saving), style = Zapara.typography.caption)
            }
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(Zapara.space.s)) {
                ZButton(stringResource(R.string.avatar_change), { pickingFor = target; picker.launch("image/*") },
                    modifier = Modifier.fillMaxWidth(), enabled = enabled && !busy, tag = "Avatar.Choose",
                    leadingIcon = R.drawable.ic_avatar_photo)
                ZButton(stringResource(R.string.avatar_remove), { confirm = true }, enabled = enabled && !busy,
                    modifier = Modifier.fillMaxWidth(), ghost = true, tag = "Avatar.Remove", leadingIcon = R.drawable.ic_trash)
            }
            feedback?.let { Text(it, style = Zapara.typography.caption,
                color = if (failed) Zapara.colors.bad else Zapara.colors.text2,
                modifier = Modifier.testTag("Avatar.Feedback")) }
        }
    } else Column(modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            ChatAvatar(name, target, 56.dp)
            Column(Modifier.weight(1f)) {
                Text(stringResource(if (target.kind == AvatarKind.Group) R.string.avatar_group else R.string.avatar_profile),
                    style = Zapara.typography.bodyStrong)
                Text(stringResource(if (target.kind == AvatarKind.Group) R.string.avatar_group_hint else R.string.avatar_hint),
                    style = Zapara.typography.caption, color = Zapara.colors.text2)
            }
        }
        if (busy) Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
            Text(stringResource(R.string.avatar_saving), style = Zapara.typography.caption)
        }
        FlowRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            ZButton(stringResource(R.string.avatar_change), { pickingFor = target; picker.launch("image/*") },
                enabled = enabled && !busy, ghost = true, tag = "Avatar.Choose")
            ZButton(stringResource(R.string.avatar_remove), { confirm = true }, enabled = enabled && !busy,
                ghost = true, tag = "Avatar.Remove")
        }
        feedback?.let { Text(it, style = Zapara.typography.caption, color = if (failed) Zapara.colors.bad else Zapara.colors.text2,
            modifier = Modifier.testTag("Avatar.Feedback")) }
    }
    if (confirm) AlertDialog(onDismissRequest = { confirm = false },
        title = { Text(stringResource(R.string.avatar_remove_title)) },
        text = { Text(stringResource(R.string.avatar_remove_hint)) },
        confirmButton = { TextButton(onClick = { confirm = false; perform({ store.remove(target) }, R.string.avatar_removed) }) {
            Text(stringResource(R.string.avatar_remove)) } },
        dismissButton = { TextButton(onClick = { confirm = false }) { Text(stringResource(R.string.account_cancel)) } })
}

/** Decode with a pixel budget and shrink locally before uploading a phone photo. */
private suspend fun prepareAvatar(context: Context, uri: Uri): ByteArray = withContext(Dispatchers.IO) {
    val raw = context.contentResolver.openInputStream(uri)?.use { HttpBodies.readLimited(it, 20 * 1024 * 1024) }
        ?: throw IllegalArgumentException("Missing image")
    val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
    BitmapFactory.decodeByteArray(raw, 0, raw.size, bounds)
    require(bounds.outWidth > 0 && bounds.outHeight > 0 && bounds.outWidth.toLong() * bounds.outHeight <= 48_000_000)
    require(bounds.outMimeType in setOf("image/jpeg", "image/png", "image/webp"))
    var sample = 1
    while (maxOf(bounds.outWidth, bounds.outHeight) / sample > 1024) sample *= 2
    val decoded = BitmapFactory.decodeByteArray(raw, 0, raw.size, BitmapFactory.Options().apply { inSampleSize = sample })
        ?: throw IllegalArgumentException("Invalid image")
    var oriented: Bitmap? = null
    var square: Bitmap? = null
    try {
        val orientation = runCatching { ExifInterface(ByteArrayInputStream(raw)).getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL) }
            .getOrDefault(ExifInterface.ORIENTATION_NORMAL)
        val matrix = Matrix().apply {
            when (orientation) {
                ExifInterface.ORIENTATION_FLIP_HORIZONTAL -> setScale(-1f, 1f)
                ExifInterface.ORIENTATION_ROTATE_180 -> setRotate(180f)
                ExifInterface.ORIENTATION_FLIP_VERTICAL -> setScale(1f, -1f)
                ExifInterface.ORIENTATION_TRANSPOSE -> { setRotate(90f); postScale(-1f, 1f) }
                ExifInterface.ORIENTATION_ROTATE_90 -> setRotate(90f)
                ExifInterface.ORIENTATION_TRANSVERSE -> { setRotate(270f); postScale(-1f, 1f) }
                ExifInterface.ORIENTATION_ROTATE_270 -> setRotate(270f)
            }
        }
        oriented = Bitmap.createBitmap(decoded, 0, 0, decoded.width, decoded.height, matrix, true)
        val prepared = android.media.ThumbnailUtils.extractThumbnail(oriented, 512, 512)
        square = prepared
        ByteArrayOutputStream().use { output ->
            check(prepared.compress(Bitmap.CompressFormat.WEBP, 90, output))
            output.toByteArray().also { require(it.size <= AvatarHttpClient.maxUploadBytes) }
        }
    } finally {
        square?.takeIf { it !== oriented && it !== decoded }?.recycle()
        oriented?.takeIf { it !== decoded }?.recycle()
        decoded.recycle()
    }
}
