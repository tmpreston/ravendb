package raven.linq.dsl.extractors;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

import java.util.HashSet;
import java.util.Set;

import org.junit.Test;

import com.mysema.query.support.Expressions;
import com.mysema.query.types.expr.BooleanExpression;
import com.mysema.query.types.expr.StringExpression;
import com.mysema.query.types.template.StringTemplate;

import raven.linq.dsl.expressions.AnonymousExpression;
import raven.linq.dsl.visitors.RootsExtractor;
import raven.linq.dsl.visitors.RootsExtractorContext;
import raven.samples.PersonResult;
import raven.samples.QPerson;
import raven.samples.QPersonResult;

public class RootsExtractorTest {

  @Test
  public void testTemplate() {
    StringExpression stringExpression = StringTemplate.create("c => c.firstname");
    RootsExtractorContext context = new RootsExtractorContext();
    stringExpression.accept(RootsExtractor.DEFAULT, context);

    assertTrue(context.getRoots().isEmpty());
  }

  @Test
  public void testAnonymous() {
    QPerson p = QPerson.person;
    QPersonResult pr = QPersonResult.personResult;


    AnonymousExpression anonymousExpression = new AnonymousExpression()
    .with(pr.name, p.firstname)
    .with(pr.count, Expressions.constant(1));

    RootsExtractorContext context = new RootsExtractorContext();
    anonymousExpression.accept(RootsExtractor.DEFAULT, context);

    Set<String> expected = new HashSet<>();
    expected.add("person");

    assertEquals(expected, context.getRoots());
  }

  @Test
  public void testSimple() {
    QPerson person = QPerson.person;

    RootsExtractorContext context = new RootsExtractorContext();
    BooleanExpression booleanExpression = person.firstname.eq("John");
    booleanExpression.accept(RootsExtractor.DEFAULT, context);

    Set<String> expected = new HashSet<>();
    expected.add("person");

    assertEquals(expected, context.getRoots());
  }

  @Test
  public void testMultiple() {
    QPerson p1 = new QPerson("p1");
    QPerson p2 = new QPerson("p2");


    RootsExtractorContext context = new RootsExtractorContext();
    BooleanExpression booleanExpression = p1.firstname.eq(p2.lastname);
    booleanExpression.accept(RootsExtractor.DEFAULT, context);

    Set<String> expected = new HashSet<>();
    expected.add("p1");
    expected.add("p2");

    assertEquals(expected, context.getRoots());

  }


}
