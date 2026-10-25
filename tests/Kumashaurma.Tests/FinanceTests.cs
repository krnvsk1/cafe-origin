using Kumashaurma.API.Services.Edo;
using Kumashaurma.API.Services.Finance;
using Xunit;

namespace Kumashaurma.Tests
{
    public class FinanceTests
    {
        // Как у «Метро»: реквизиты продавца, договор основанием, отсрочка в доп. сведениях по строкам
        private const string UpdWithTerms = @"<Файл ИдФайл=""ON_NSCHFDOPPR_terms"" ВерсФорм=""5.03"">
  <Документ>
    <СвСчФакт НомерДок=""9610"" ДатаДок=""12.10.2026"">
      <СвПрод>
        <ИдСв><СвЮЛУч НаимОрг=""ООО Метро"" ИННЮЛ=""7704218694"" КПП=""500301001""/></ИдСв>
        <БанкРекв НомерСчета=""40702810900000000005""><СвБанк НаимБанк=""АО Банк"" БИК=""044525700"" КорСчет=""30101810200000000700""/></БанкРекв>
      </СвПрод>
    </СвСчФакт>
    <ТаблСчФакт>
      <СведТов НомСтр=""1"" НаимТов=""Сыр"" ОКЕИ_Тов=""166"" КолТов=""1"" ЦенаТов=""1000"" СтТовБезНДС=""1000"" НалСт=""22%"" СтТовУчНал=""1220"">
        <СумНал><СумНал>220</СумНал></СумНал>
        <ИнфПолФХЖ2 Идентиф=""Отсрочка"" Значен=""14 дн.""/>
      </СведТов>
      <ВсегоОпл СтТовУчНалВсего=""1220""><СумНалВсего><СумНал>220</СумНал></СумНалВсего></ВсегоОпл>
    </ТаблСчФакт>
    <СвПродПер><СвПер СодОпер=""Товары переданы"" ДатаПер=""12.10.2026""><ОснПер РеквНаимДок=""Договор поставки"" РеквНомерДок=""№ 06/03-2025"" РеквДатаДок=""06.03.2025""/></СвПер></СвПродПер>
  </Документ>
</Файл>";

        // Явный срок оплаты в доп. сведениях документа, основание — заказ (не договор)
        private const string UpdExplicitDue = @"<Файл ИдФайл=""ON_NSCHFDOPPR_due"">
  <Документ>
    <СвСчФакт НомерДок=""77"" ДатаДок=""20.10.2026"">
      <ИнфПолФХЖ1><ТекстИнф Идентиф=""СрокОплаты"" Значен=""30.10.2026""/></ИнфПолФХЖ1>
    </СвСчФакт>
    <ТаблСчФакт>
      <СведТов НомСтр=""1"" НаимТов=""Лаваш"" ОКЕИ_Тов=""796"" КолТов=""10"" ЦенаТов=""10"" СтТовБезНДС=""100"" НалСт=""10%"" СтТовУчНал=""110""/>
      <ВсегоОпл СтТовУчНалВсего=""110""/>
    </ТаблСчФакт>
    <СвПродПер><СвПер><ОснПер РеквНаимДок=""Заказ клиента"" РеквНомерДок=""Л-1"" РеквДатаДок=""19.10.2026""/></СвПер></СвПродПер>
  </Документ>
</Файл>";

        [Fact]
        public void ParsesBankBasisAndDelay()
        {
            var u = UpdParser.Parse(UpdWithTerms);

            Assert.Equal("40702810900000000005", u.SellerBank!.Account);
            Assert.Equal("044525700", u.SellerBank.Bik);
            Assert.Equal("30101810200000000700", u.SellerBank.CorrAccount);
            Assert.True(u.Basis!.IsContract);
            Assert.Equal(14, u.PaymentDelayDays);
            Assert.Equal(new DateTime(2026, 10, 26), u.DueDate!.Value.Date);
            // «№» из номера договора не удваивается
            Assert.Equal(
                "Оплата по УПД № 9610 от 12.10.2026 по договору № 06/03-2025 от 06.03.2025. Сумма 1 220,00 руб., в т.ч. НДС 22% — 220,00 руб.",
                UpdParser.PaymentPurpose(u));
        }

        [Fact]
        public void ExplicitDueDateWins_AndOrderIsNotContract()
        {
            var u = UpdParser.Parse(UpdExplicitDue);

            Assert.Null(u.PaymentDelayDays);
            Assert.Equal(new DateTime(2026, 10, 30), u.DueDate!.Value.Date);
            Assert.Null(u.SellerBank);
            Assert.False(u.Basis!.IsContract);
            Assert.DoesNotContain("договору", UpdParser.PaymentPurpose(u));
        }

        [Fact]
        public void DueDate_FromUpdOrSupplierDelay()
        {
            var docDate = new DateTime(2026, 10, 12);
            Assert.Equal(new DateTime(2026, 10, 26), PaymentSchedule.DueDate(new DateTime(2026, 10, 26), docDate, supplierDelayDays: 30));
            Assert.Equal(new DateTime(2026, 11, 11), PaymentSchedule.DueDate(null, docDate, supplierDelayDays: 30));
            Assert.Null(PaymentSchedule.DueDate(null, docDate, supplierDelayDays: null));
        }

        [Theory]
        [InlineData("2026-10-24", false, PaymentBuckets.Overdue, -1)]
        [InlineData("2026-10-25", false, PaymentBuckets.ThisWeek, 0)]
        [InlineData("2026-11-01", false, PaymentBuckets.ThisWeek, 7)]
        [InlineData("2026-11-02", false, PaymentBuckets.Later, 8)]
        [InlineData("2026-10-01", true, PaymentBuckets.Paid, -24)]
        public void Buckets(string due, bool paid, string bucket, int daysLeft)
        {
            var today = new DateTime(2026, 10, 25);
            var dueDate = DateTime.Parse(due);
            Assert.Equal(bucket, PaymentSchedule.Bucket(dueDate, paid, today));
            Assert.Equal(daysLeft, PaymentSchedule.DaysLeft(dueDate, today));
            Assert.Equal(PaymentBuckets.NoDueDate, PaymentSchedule.Bucket(null, false, today));
        }

        [Fact]
        public void TodayIsMoscowDate()
        {
            // 22:30 UTC 24.10 — в Москве уже 25.10
            Assert.Equal(new DateTime(2026, 10, 25), PaymentSchedule.TodayMoscow(new DateTime(2026, 10, 24, 22, 30, 0, DateTimeKind.Utc)));
        }
    }
}
