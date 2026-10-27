using System.Text.Json;
using StoryPlatform.Contracts.AI.Models;
using Xunit;

namespace StoryPlatform.UnitTests;

public sealed class LearningArtifactGroundingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SmallBatchesDistributeTypesInsteadOfRequestingMoreTypesThanItems(int count)
    {
        var kinds=new[]{"quiz_facts","quiz_reasoning","quiz_vocabulary"};
        var required=kinds.SelectMany(kind=>LearningBatchSpec.QuizTypesForBatch(kind,count)).ToArray();
        Assert.Equal(3,required.Distinct().Count());
        Assert.All(kinds,kind=>Assert.True(LearningBatchSpec.QuizTypesForBatch(kind,count).Count<=count));
    }
    [Fact]
    public void RequiredQuizTypesAreCheckedBeforeCachingBatchResults()
    {
        var items=Enumerable.Range(0,3).Select(i=>ValidQuiz with {Question=$"Na kiên nhẫn sửa xe {i}?"}).ToArray();
        var spec=new LearningBatchSpec {Kind="quiz_facts",TargetCount=3,RequiredQuestionTypes=["multiple_choice","true_false","short_answer"]};
        var error=Assert.Throws<JsonException>(()=>LearningArtifactGrounding.Quiz(Story,items,spec,""));
        Assert.Equal("QUIZ_TYPE_COVERAGE_INVALID",error.Message);
        Assert.NotEmpty(LearningValidationFeedback.Instruction(error.Message));
    }

    [Fact]
    public void AQuotationDoesNotMakeAnUnrelatedQuestionValid()
    {
        var spec=new LearningBatchSpec {Kind="quiz_facts",TargetCount=1,RequiredQuestionTypes=["short_answer"]};
        var error=Assert.Throws<JsonException>(()=>LearningArtifactGrounding.Quiz(Story,
            [ValidQuiz with {Question="Unrelated astronomy question?"}],spec,""));
        Assert.Equal("QUIZ_QUESTION_RELEVANCE_INVALID",error.Message);Assert.Equal(0,error.Data["LearningItemIndex"]);
    }
    private static QuizItemDto ValidQuiz => new()
    {
        Type = "short_answer", Question = "Na sửa gì?", CorrectAnswer = "xe",
        SourceQuote = "Na kiên nhẫn sửa xe.", Explanation = "Na sửa xe.", Hint = "Đọc câu đầu.", LearningGoal = "recall"
    };

    [Theory]
    [InlineData("item", "QUIZ_ITEM_REQUIRED")]
    [InlineData("question", "QUIZ_QUESTION_REQUIRED")]
    [InlineData("explanation", "QUIZ_EXPLANATION_REQUIRED")]
    [InlineData("hint", "QUIZ_HINT_REQUIRED")]
    [InlineData("goal", "QUIZ_LEARNING_GOAL_INVALID")]
    public void QuizIdentifiesTheInvalidFieldAndZeroBasedItemIndex(string field, string code)
    {
        var item = ValidQuiz with { Question = "Na kiên nhẫn làm gì?" };
        item = field switch
        {
            "item" => null!, "question" => item with { Question = " " },
            "explanation" => item with { Explanation = "" }, "hint" => item with { Hint = " " },
            _ => item with { LearningGoal = "Ghi nhớ" }
        };
        var error = Assert.Throws<JsonException>(() => LearningArtifactGrounding.Quiz(Story, [ValidQuiz, item],
            new() { Kind = "quiz_facts", TargetCount = 2 }, ""));
        Assert.Equal(code, error.Message);
        Assert.Equal(1, error.Data["LearningItemIndex"]);
        Assert.Equal(code, LearningValidationFeedback.Normalize(code));
        Assert.NotEmpty(LearningValidationFeedback.Instruction(code));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuizRejectsNormalizedDuplicatesWithinTheBatchAndExcludedQuestions(bool excluded)
    {
        QuizItemDto[] items = excluded ? [ValidQuiz] : [ValidQuiz, ValidQuiz with { Question = "NA SỬA GÌ!!!" }];
        var error = Assert.Throws<JsonException>(() => LearningArtifactGrounding.Quiz(Story, items,
            new() { Kind = "quiz_facts", TargetCount = items.Length, ExcludedQuestions = excluded ? ["NA SỬA GÌ!!!"] : [] }, ""));
        Assert.Equal("QUIZ_QUESTION_DUPLICATE", error.Message);
        Assert.Equal(excluded ? 0 : 1, error.Data["LearningItemIndex"]);
    }

    private static StoryContentDto Story => new() { Title = "Na", StorySections = [new(1, "", "Na kiên nhẫn sửa xe. Nam tìm bánh.")] };
    private static LearningBatchSpec Spec => new() { TargetCount = 2, Kind = "vocabulary" };

    [Theory]
    // The model collapsed or duplicated spacing.
    [InlineData("Na kiên nhẫn sửa xe.  Nam tìm bánh.")]
    // A non-breaking space stood in for an ordinary one.
    [InlineData("Na kiên\u00A0nhẫn sửa xe. Nam tìm bánh.")]
    // An invisible zero-width space was injected after the sentence.
    [InlineData("Na kiên nhẫn sửa xe.\u200B Nam tìm bánh.")]
    // Decomposed Vietnamese diacritics, as emitted by a non-NFC source.
    [InlineData("Na kie\u0302n nha\u0302\u0303n su\u031B\u0309a xe. Nam tìm bánh.")]
    // The model wrapped its evidence in decorative quotation marks of its own.
    [InlineData("\u201CNa kiên nhẫn sửa xe. Nam tìm bánh.\u201D")]
    // Leading and trailing padding.
    [InlineData("  Na kiên nhẫn sửa xe. Nam tìm bánh.  ")]
    public void AnchorRecoversQuotesThatOnlyDifferInRepresentation(string quote)
    {
        var (start, end) = LearningArtifactGrounding.Anchor(StoryContent, quote);
        // Whatever the model emitted, the range always covers the verbatim story text.
        Assert.Equal(StoryContent, StoryContent[start..end]);
    }

    [Fact]
    public void AnchorStillRejectsAPassageTheStoryDoesNotContain()
    {
        var error = Assert.Throws<JsonException>(() => LearningArtifactGrounding.Anchor(StoryContent, "Na kiên nhẫn rửa xe."));
        Assert.Equal("LEARNING_EVIDENCE_NOT_IN_STORY", error.Message);
        Assert.False(LearningArtifactGrounding.TryAnchor(StoryContent, "Na kiên nhẫn rửa xe.", out _));
        // An empty quotation is a different failure: the model supplied no evidence at all.
        Assert.Equal("LEARNING_EVIDENCE_REQUIRED",
            Assert.Throws<JsonException>(() => LearningArtifactGrounding.Anchor(StoryContent, "   ")).Message);
    }

    [Fact]
    public void AnchorMapsWhitespaceOnlyDifferencesBackToTheOriginalOffsets()
    {
        Assert.True(LearningArtifactGrounding.TryAnchor(StoryContent, "  Nam   tìm\tbánh. ", out var range));
        Assert.Equal("Nam tìm bánh.", StoryContent[range.Start..range.End]);
    }

    [Fact]
    public void QuizRepairsANearMissQuoteToVerbatimStoryTextAndKeepsTheItem()
    {
        var items = LearningArtifactGrounding.Quiz(Story,
            [ValidQuiz with { SourceQuote = "  Na kiên nhẫn sửa xe.  " }, ValidQuiz with { Question = "Nam tìm gì?", SourceQuote = "Nam tìm bánh." }],
            new() { Kind = "quiz_facts", TargetCount = 2 }, "");
        Assert.Equal("Na kiên nhẫn sửa xe.", items[0].SourceQuote);
        // The repaired quotation is an exact ordinal match, so persisted offsets cannot drift.
        var (start, end) = LearningArtifactGrounding.Anchor(StoryContent, items[0].SourceQuote);
        Assert.Equal(0, start);
        Assert.Equal(items[0].SourceQuote, StoryContent[start..end]);
    }

    [Fact]
    public void VocabularyRepairsNearMissEvidenceSoItAnchorsExactly()
    {
        var items = LearningArtifactGrounding.Vocabulary(Story,
            [new("kiên nhẫn", "Không bỏ cuộc ngay") { SourceQuote = "Na kie\u0302n nha\u0302\u0303n su\u031B\u0309a xe." }],
            Spec, "Only one suitable term for this objective");
        Assert.Equal("Na kiên nhẫn sửa xe.", items[0].SourceQuote);
    }

    [Fact]
    public void QuizStillRejectsAGenuinelyInventedQuotation()
    {
        var error = Assert.Throws<JsonException>(() => LearningArtifactGrounding.Quiz(Story,
            [ValidQuiz with { SourceQuote = "Na kiên nhẫn rửa xe." }], new() { Kind = "quiz_facts", TargetCount = 1 }, ""));
        Assert.Equal("LEARNING_EVIDENCE_NOT_IN_STORY", error.Message);
        Assert.Equal(0, error.Data["LearningItemIndex"]);
    }

    private static string StoryContent => Story.StorySections[0].Content;

    [Theory]
    [InlineData("Na", "Không có câu này.")]
    [InlineData("kiên", "Nam tìm bánh.")]
    [InlineData("an", "Nam tìm bánh.")]
    public void VocabularyRejectsInventedEvidenceMismatchedQuotesAndSubstringTerms(string term, string quote)
    {
        Assert.Throws<JsonException>(() => LearningArtifactGrounding.Vocabulary(Story,
            [new(term, "Nghĩa") { SourceQuote = quote }], Spec, "Insufficient source material"));
    }
    [Fact]
    public void ShortfallIsAllowedOnlyWithAReasonAndGroundedDistinctTerms()
    {
        GeneratedVocabularyItemDto[] items = [new("kiên nhẫn", "Không bỏ cuộc ngay") { SourceQuote = "Na kiên nhẫn sửa xe." }];
        Assert.Throws<JsonException>(() => LearningArtifactGrounding.Vocabulary(Story, items, Spec, ""));
        LearningArtifactGrounding.Vocabulary(Story, items, Spec, "Only one suitable term for this objective");
        Assert.Throws<JsonException>(() => LearningArtifactGrounding.Vocabulary(Story, [items[0], items[0]], Spec, ""));
        Assert.Equal(7, StoryWordCounter.Count("Na kiên nhẫn sửa xe. Nam tìm."));
    }
    [Theory]
    [InlineData("6-8", 810, 990)]
    [InlineData("9-10", 1350, 1650)]
    public void PinnedAgeBoundsAreNotTheLegacySeventyPercentMinimum(string age, int minimum, int maximum)
    {
        var profile = LearningGenerationProfile.ForAge(age);
        Assert.Equal(minimum, profile.MinimumWords); Assert.Equal(maximum, profile.MaximumWords);
    }
}
