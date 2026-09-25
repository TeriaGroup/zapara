package ru.bgtu_voenmeh.zapara.data.communities

import kotlinx.coroutines.runBlocking
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.testToken
import ru.bgtu_voenmeh.zapara.data.api.FakeHttp
import ru.bgtu_voenmeh.zapara.data.api.HttpReply
import ru.bgtu_voenmeh.zapara.data.api.jsonReply

class CommunityChannelClientTest {
    private val community = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
    private val conversation = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    private val topic = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
    private val message = "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
    private val ballot = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"
    private val option = "ffffffff-ffff-4fff-8fff-ffffffffffff"
    private val role = "22222222-2222-4222-8222-222222222222"
    private val peer = "33333333-3333-4333-8333-333333333333"
    private val token = testToken("za_", 4)
    private val base = "http://127.0.0.1:9/api/v2/communities"

    @Test fun topic_list_keeps_channel_types_and_general_id() = runBlocking {
        val http = FakeHttp { call ->
            assertEquals("GET", call.method)
            assertEquals("$base/$community/topics?typed=1", call.url)
            jsonReply("""{"topics":[{"topicId":null,"title":"Общий","icon":"💬","kind":"chat","lastBody":null,"lastAuthor":null,"lastAt":null,"unread":1,"canDelete":false,"activeBallots":0,"description":"","accent":"default","pinned":false,"writePolicy":"all","canPost":true},{"topicId":"$topic","title":"Решения","icon":"🗳","kind":"ballots","lastBody":null,"lastAuthor":null,"lastAt":null,"unread":0,"canDelete":false,"activeBallots":2,"description":"Важные решения","accent":"purple","pinned":true,"writePolicy":"managers","canPost":false}],"canManageChannels":false}""")
        }
        val response = client(http).topics(token, community)
        assertFalse(response.canManageChannels)
        val topics = response.topics
        assertNull(topics.first().topicId)
        assertEquals("chat", topics.first().kind)
        assertEquals(1, topics.first().unread)
        assertEquals(topic, topics.last().topicId)
        assertEquals("ballots", topics.last().kind)
        assertEquals(2, topics.last().activeBallots)
        assertEquals("Важные решения", topics.last().description)
        assertEquals("purple", topics.last().accent)
        assertTrue(topics.last().pinned)
        assertEquals("managers", topics.last().writePolicy)
        assertFalse(topics.last().canPost)
    }

    @Test fun renaming_preserves_explicit_channel_customization() = runBlocking {
        val http = FakeHttp { call ->
            assertEquals("POST", call.method)
            assertEquals("$base/$community/topics/$topic?typed=1", call.url)
            assertEquals("""{"title":"Решения","icon":"🗳","kind":"ballots","description":"Важные решения","accent":"purple","pinned":true,"writePolicy":"managers"}""", String(call.body!!))
            jsonReply("""{"topics":[{"topicId":null,"title":"Общий","icon":"💬","kind":"chat","lastBody":null,"lastAuthor":null,"lastAt":null,"unread":0,"canDelete":false,"activeBallots":0,"description":"","accent":"default","pinned":false,"writePolicy":"all","canPost":true},{"topicId":"$topic","title":"Решения","icon":"🗳","kind":"ballots","lastBody":null,"lastAuthor":null,"lastAt":null,"unread":0,"canDelete":true,"activeBallots":1,"description":"Важные решения","accent":"purple","pinned":true,"writePolicy":"managers","canPost":true}],"canManageChannels":true}""")
        }
        val renamed = client(http).renameTopic(token, community, topic, "Решения", "🗳", "ballots",
            "Важные решения", "purple", true, "managers")
        assertTrue(renamed.topics.last().pinned)
    }

    @Test fun scoped_chat_reads_and_writes_only_selected_topic() = runBlocking {
        val http = FakeHttp { call -> when (call.method + " " + call.url.removePrefix(base)) {
            "GET /conversations/$conversation/messages?topic=$topic" -> jsonReply("""{"messages":[],"hasMore":false}""")
            "GET /conversations/$conversation/messages?topic=general&before=$message" -> jsonReply("""{"messages":[],"hasMore":false}""")
            "POST /conversations/$conversation/topic-messages" -> {
                assertEquals("""{"body":"Привет","topicId":"$topic"}""", String(call.body!!))
                HttpReply(201, messageJson().toByteArray())
            }
            else -> error(call.url)
        } }
        val api = client(http)
        assertFalse(api.messages(token, conversation, topic = topic).hasMore)
        assertFalse(api.messages(token, conversation, before = message, topic = "general").hasMore)
        assertEquals(message, api.sendTopicMessage(token, conversation, "Привет", topic).messageId)
    }

    @Test fun reply_in_topic_uses_valid_json_property_name() = runBlocking {
        val http = FakeHttp { call ->
            assertEquals("POST", call.method)
            assertEquals("$base/conversations/$conversation/topic-messages", call.url)
            assertEquals("""{"body":"Ответ","topicId":"$topic","replyTo":"$message"}""", String(call.body!!))
            HttpReply(201, messageJson().toByteArray())
        }
        assertEquals(message, client(http).sendTopicMessage(token, conversation, "Ответ", topic, message).messageId)
    }

    @Test fun topic_media_sets_header_only_for_custom_chat_topic() = runBlocking {
        val http = FakeHttp { call ->
            assertEquals("POST", call.method)
            assertEquals("$base/conversations/$conversation/media", call.url)
            assertEquals(topic, call.headers["X-Zapara-Topic"])
            HttpReply(201, messageJson().toByteArray())
        }
        client(http).sendMedia(token, conversation, "image", "photo.png", byteArrayOf(1), topicId = topic)
        assertEquals(1, http.requests.size)
    }

    @Test fun ballot_channel_reads_scoped_board_and_votes_with_existing_route() = runBlocking {
        val http = FakeHttp { call -> when (call.method + " " + call.url.removePrefix(base)) {
            "GET /$community/ballots?topic=$topic" -> jsonReply(boardJson())
            "POST /$community/ballots/$ballot/votes" -> {
                assertEquals("""{"optionId":"$option"}""", String(call.body!!))
                jsonReply(boardJson(chosen = true))
            }
            else -> error(call.url)
        } }
        val api = client(http)
        val board = api.ballots(token, community, topic)
        assertTrue(board.canOpen)
        assertEquals("open", board.ballots.single().status)
        assertEquals(topic, board.ballots.single().topicId)
        assertEquals(1, board.ballots.single().options.single().votes)
        assertTrue(api.voteBallot(token, community, ballot, option).ballots.single().options.single().chosen)
    }

    @Test fun proposed_ballot_is_addressed_to_selected_channel() = runBlocking {
        val http = FakeHttp { call ->
            assertEquals("POST", call.method)
            assertEquals("$base/$community/ballots/collective", call.url)
            assertEquals("""{"question":"Когда?","options":["Сегодня","Завтра"],"days":3,"topicId":"$topic"}""", String(call.body!!))
            HttpReply(201, boardJson().toByteArray())
        }
        assertEquals(topic, client(http).proposeBallot(token, community, "Когда?", listOf("Сегодня", "Завтра"), 3, topic).ballots.single().topicId)
    }

    @Test fun trusted_channel_power_uses_existing_desk_roles_and_grants() = runBlocking {
        val http = FakeHttp { call -> when (call.method + " " + call.url.removePrefix(base)) {
            "GET /$community/desk" -> jsonReply(deskJson(false))
            "POST /$community/roles/$role/powers" -> {
                assertEquals("""{"power":"channels","enabled":true}""", String(call.body!!))
                jsonReply(deskJson(true))
            }
            "POST /$community/roles/$role/grants" -> {
                assertEquals("""{"userId":"$peer"}""", String(call.body!!))
                jsonReply(deskJson(true, true))
            }
            else -> error(call.url)
        } }
        val api = client(http)
        assertTrue(api.desk(token, community).headman)
        assertFalse(api.desk(token, community).powers.any { it.power == "channels" })
        assertTrue(api.setRolePower(token, community, role, true).powers.any { it.power == "channels" })
        assertEquals(peer, api.grantRole(token, community, role, peer).grants.single().userId)
    }

    private fun client(http: FakeHttp) = CommunityHttpClient(http, AccountServerScope.parse("http://127.0.0.1:9/"))
    private fun messageJson() = """{"messageId":"$message","conversationId":"$conversation","senderId":"$community","senderName":"Аня","body":"Привет","createdAt":"2026-09-25T12:00:00Z"}"""
    private fun boardJson(chosen: Boolean = false) = """{"headman":false,"canOpen":true,"canClose":false,"members":10,"supportersNeeded":3,"ballots":[{"ballotId":"$ballot","question":"Когда?","origin":"headman","status":"open","deadlineAt":"2026-10-01T12:00:00Z","supporters":0,"supportersNeeded":3,"supported":false,"options":[{"optionId":"$option","label":"Сегодня","votes":1,"chosen":$chosen}],"effect":"","outcome":"","topicId":"$topic"}]}"""
    private fun deskJson(power: Boolean, granted: Boolean = false) = """{"headman":true,"roles":[{"roleId":"$role","name":"Доверенный"}],"grants":${if (granted) "[{\"roleId\":\"$role\",\"userId\":\"$peer\"}]" else "[]"},"applicants":[],"powers":${if (power) "[{\"roleId\":\"$role\",\"power\":\"channels\"}]" else "[]"},"mine":[]}"""
}
